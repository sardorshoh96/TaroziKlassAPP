#if ANDROID
using Android.Content;
using Android.Content.PM;
using Android.OS;
#endif
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TaroziAPP.Services.Api;

namespace TaroziAPP.Services;

/// <summary>
/// Ilova yangilanishi IKKI manbadan:
///  • Server (device.radiomer.app) — har 2 daqiqada. Admin panelda "majburiy" belgilangan yangilanish darhol yuklab olinadi;
///  • GitHub Releases — har 1 soatda (eski usul, zaxira manba).
/// Uzilgan yuklash qolgan joyidan davom etadi. Yuklangan APK tekshiriladi (sha256, paket nomi, versiya yangiroqligi).
/// O'rnatish faqat tarozi BO'SH paytda (pul yo'q, to'lov kutilmayapti, yuk yo'q) — mijoz ishi va puli yo'qolmasin.
/// Avtomatik rejimda ekranga hech qanday oyna chiqmaydi (kiosk).
/// </summary>
public class AppUpdateService : IDisposable
{
    // ⚙️ GitHub
    private const string GitHubOwner = "sardorshoh96";
    private const string GitHubRepo = "TaroziKlassAPP";
    private const string GitHubApkName = "uz.taroziklass-Signed.apk";
    private const string GitHubLatestUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest";

    // ⚙️ Server
    private const string ServerBase = "https://device.radiomer.app";

    private static readonly TimeSpan ServerInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan GitHubInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan IdleWaitNormal = TimeSpan.FromMinutes(30);
    // Yuklash shuncha vaqt bir bayt ham olmasa — uziladi (keyingi tekshiruvda qolgan joyidan davom etadi)
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(90);
    private const int MaxFailuresPerVersion = 3;

    /// <summary>Tarozi bo'shmi (pul yo'q, to'lov kutilmayapti, yuk yo'q). MainPage beradi; null = bo'sh.</summary>
    public static Func<bool>? IsIdle { get; set; }

    // O'rnatish natijasi (PackageInstallReceiver) shu versiyaga yoziladi
    private static string? _lastCommittedKey;
    private static readonly Dictionary<string, int> Failures = new();
    private static readonly object FailuresGate = new();

    private readonly JsonRpcClient _rpc;
    private readonly CredentialStorageService _credentials;
    private readonly SemaphoreSlim _busy = new(1, 1);   // bir vaqtda bitta tekshiruv/yuklash
    private Timer? _serverTimer;
    private Timer? _githubTimer;
    private bool _disposed;

    private sealed record Candidate(string Source, string VersionName, long VersionCode, string Url,
                                    string? Sha256, long Size, bool Mandatory, bool UseDeviceAuth)
    {
        public string Key => $"{VersionName}/{VersionCode}";
    }

    private enum Outcome { Committed, Retry, Bad }

    public AppUpdateService(JsonRpcClient rpc, CredentialStorageService credentials)
    {
        _rpc = rpc;
        _credentials = credentials;
    }

    /// <summary>Server: 30 s dan keyin va har 2 daqiqada; GitHub: 1 daqiqadan keyin va har soatda.</summary>
    public void StartPeriodicCheck()
    {
        if (_serverTimer != null) return;
        Console.WriteLine("[AppUpdate] ⏰ Yangilanish tekshiruvi: server har 2 daqiqada, GitHub har 1 soatda");
        _serverTimer = new Timer(_ => _ = RunAsync(fromServer: true), null, TimeSpan.FromSeconds(30), ServerInterval);
        _githubTimer = new Timer(_ => _ = RunAsync(fromServer: false), null, TimeSpan.FromMinutes(1), GitHubInterval);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _serverTimer?.Dispose();
        _githubTimer?.Dispose();
    }

    /// <summary>MainActivity eski chaqiruvi uchun saqlangan (endi kerak emas).</summary>
    public void TryInstallPendingApk() { }

    /// <summary>PackageInstallReceiver: o'rnatish muvaffaqiyatsiz — shu versiyani qayta-qayta urinmaslik uchun.</summary>
    public static void ReportInstallFailure(string? message)
    {
        var key = _lastCommittedKey;
        if (key == null) return;
        lock (FailuresGate) Failures[key] = (Failures.TryGetValue(key, out var n) ? n : 0) + 1;
        Console.WriteLine($"[AppUpdate] ❌ O'rnatish muvaffaqiyatsiz ({key}): {message}");
    }

    private static long LocalVersionCode => long.TryParse(AppInfo.BuildString, out var c) ? c : 0;
    private static long LocalVersionNumber => AppInfo.Version.Major * 10000L + AppInfo.Version.Minor * 100L + Math.Max(0, AppInfo.Version.Build);

    private async Task RunAsync(bool fromServer)
    {
        if (_disposed || !await _busy.WaitAsync(0)) return;   // boshqa tekshiruv yoki yuklash davom etmoqda
        try
        {
            var c = fromServer ? await CheckServerAsync() : await CheckGitHubAsync();
            if (c == null) return;

            int failures;
            lock (FailuresGate) failures = Failures.TryGetValue(c.Key, out var n) ? n : 0;
            if (failures >= MaxFailuresPerVersion)
            {
                Console.WriteLine($"[AppUpdate] ⏭️ {c.VersionName} {failures} marta muvaffaqiyatsiz — ilova qayta ishga tushguncha urinilmaydi");
                return;
            }

            Console.WriteLine($"[AppUpdate] 🆕 {c.Source}: {c.VersionName} (kod {c.VersionCode}){(c.Mandatory ? " — MAJBURIY" : "")}");
            var outcome = await DownloadVerifyInstallAsync(c);
            if (outcome == Outcome.Bad)
                lock (FailuresGate) Failures[c.Key] = failures + 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ❌ Xato: {ex.Message}");
        }
        finally
        {
            _busy.Release();
        }
    }

    // ---------------- Manbalar ----------------

    private async Task<Candidate?> CheckServerAsync()
    {
        var creds = await _credentials.RetrieveAsync();
        if (creds == null) return null;   // hali login qilinmagan

        var request = new JsonRpcRequest("checkUpdate", new
        {
            package = AppInfo.PackageName,
            versionCode = LocalVersionCode,
            versionName = AppInfo.VersionString
        });
        var res = await _rpc.PostAsync<JsonElement>(request, creds.Login, creds.Password);
        if (!res.IsSuccess || res.Data.ValueKind != JsonValueKind.Object) return null;

        var d = res.Data;
        if (!d.TryGetProperty("available", out var av) || av.ValueKind != JsonValueKind.True) return null;

        long code = d.TryGetProperty("versionCode", out var vc) && vc.TryGetInt64(out var cc) ? cc : 0;
        if (code <= LocalVersionCode) return null;
        string? path = d.TryGetProperty("path", out var p) ? p.GetString() : null;
        if (string.IsNullOrEmpty(path)) return null;

        return new Candidate(
            Source: "Server",
            VersionName: d.TryGetProperty("versionName", out var vn) && vn.ValueKind == JsonValueKind.String ? vn.GetString()! : code.ToString(),
            VersionCode: code,
            Url: ServerBase + path,
            Sha256: d.TryGetProperty("sha256", out var sh) && sh.ValueKind == JsonValueKind.String ? sh.GetString() : null,
            Size: d.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s) ? s : 0,
            Mandatory: d.TryGetProperty("mandatory", out var m) && m.ValueKind == JsonValueKind.True,
            UseDeviceAuth: true);
    }

    private async Task<Candidate?> CheckGitHubAsync()
    {
        var tag = await GetLatestGitHubTagAsync();
        if (tag == null) return null;
        long remote = ParseVersion(tag);
        Console.WriteLine($"[AppUpdate] GitHub: {tag} (hozirgi {AppInfo.VersionString})");
        if (remote <= LocalVersionNumber) return null;

        return new Candidate(
            Source: "GitHub",
            VersionName: tag.TrimStart('v', 'V'),
            VersionCode: 0,   // GitHub versiya kodini bermaydi — APK ichidan tekshiriladi
            Url: $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{tag}/{GitHubApkName}",
            Sha256: null,
            Size: 0,
            Mandatory: false,
            UseDeviceAuth: false);
    }

    /// <summary>releases/latest ning yo'naltirish manzilidan teg nomi (API limitisiz).</summary>
    private static async Task<string?> GetLatestGitHubTagAsync()
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", "TaroziAPP-Updater");
            using var response = await client.GetAsync(GitHubLatestUrl);
            if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.MovedPermanently)
            {
                var location = response.Headers.Location?.ToString();
                var i = location?.LastIndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase) ?? -1;
                if (i >= 0) return location![(i + "/releases/tag/".Length)..];
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] GitHub tekshirilmadi: {ex.Message}");
            return null;
        }
    }

    // ---------------- Yuklash → tekshirish → o'rnatish ----------------

    private async Task<Outcome> DownloadVerifyInstallAsync(Candidate c)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, "updates");
        Directory.CreateDirectory(dir);
        var safeName = new string(c.VersionName.Where(ch => char.IsLetterOrDigit(ch) || ch == '.').ToArray());
        var final = Path.Combine(dir, $"update-{c.Source}-{safeName}-{c.VersionCode}.apk");
        var part = final + ".part";

        // Boshqa (eski) versiyalarning qoldiqlarini tozalash
        foreach (var f in Directory.GetFiles(dir))
            if (f != final && f != part) TryDelete(f);

        if (!File.Exists(final))
        {
            if (!await DownloadAsync(c, part)) return Outcome.Retry;   // uzildi — keyin davom etadi

            if (c.Size > 0 && new FileInfo(part).Length != c.Size)
            {
                Console.WriteLine("[AppUpdate] ❌ Hajm mos emas — fayl qaytadan yuklanadi");
                TryDelete(part);
                return Outcome.Bad;
            }
            if (c.Sha256 != null && !string.Equals(Sha256Of(part), c.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[AppUpdate] ❌ SHA-256 mos emas — fayl buzilgan, qaytadan yuklanadi");
                TryDelete(part);
                return Outcome.Bad;
            }
            File.Move(part, final, true);
        }

        if (!VerifyApk(final, out var reason))
        {
            Console.WriteLine($"[AppUpdate] ❌ APK rad etildi: {reason}");
            TryDelete(final);
            return Outcome.Bad;
        }

        // O'rnatish ilovani qayta ishga tushiradi — faqat bo'sh paytda. Majburiy bo'lsa kutish cheksiz.
        if (!await WaitUntilIdleAsync(c.Mandatory ? Timeout.InfiniteTimeSpan : IdleWaitNormal))
        {
            Console.WriteLine("[AppUpdate] ⏳ Tarozi band — o'rnatish keyingi tekshiruvga qoldi (fayl tayyor)");
            return Outcome.Retry;
        }

        _lastCommittedKey = c.Key;
#if ANDROID
        Console.WriteLine($"[AppUpdate] 📦 {c.VersionName} o'rnatilmoqda...");
        InstallApkSilently(final);
#endif
        return Outcome.Committed;
    }

    /// <summary>Qolgan joyidan davom ettirib yuklaydi. To'liq yuklansa true, uzilsa false (.part saqlanadi).</summary>
    private async Task<bool> DownloadAsync(Candidate c, string part)
    {
        long have = File.Exists(part) ? new FileInfo(part).Length : 0;
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var req = new HttpRequestMessage(HttpMethod.Get, c.Url);
            req.Headers.UserAgent.ParseAdd("TaroziAPP-Updater");
            AppVersionHeader.AddTo(req);
            if (c.UseDeviceAuth)
            {
                var creds = await _credentials.RetrieveAsync();
                if (creds == null) return false;
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{creds.Login}:{creds.Password}")));
            }
            if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);

            using var cts = new CancellationTokenSource(StallTimeout);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // .part allaqachon to'liq (yoki fayl o'zgargan)
                if (c.Size > 0 && have == c.Size) return true;
                TryDelete(part);
                return false;
            }
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[AppUpdate] ⚠️ {c.Source} yuklash: HTTP {(int)resp.StatusCode}");
                return false;
            }

            bool append = resp.StatusCode == HttpStatusCode.PartialContent && have > 0;
            if (!append) have = 0;
            long total = resp.Content.Headers.ContentLength is long len ? len + have : c.Size;
            Console.WriteLine(append
                ? $"[AppUpdate] ⬇️ {c.Source}: davom etmoqda {have / 1024 / 1024}/{total / 1024 / 1024} MB"
                : $"[AppUpdate] ⬇️ {c.Source}: yuklanmoqda {total / 1024 / 1024} MB");

            await using var input = await resp.Content.ReadAsStreamAsync(cts.Token);
            await using var output = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920);
            var buffer = new byte[81920];
            long done = have;
            int lastTenth = total > 0 ? (int)(done * 10 / total) : -1;
            int n;
            while ((n = await input.ReadAsync(buffer, cts.Token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), CancellationToken.None);
                done += n;
                cts.CancelAfter(StallTimeout);   // ma'lumot kelyapti — kutish taymerini yangilaymiz
                if (total > 0 && (int)(done * 10 / total) != lastTenth)
                {
                    lastTenth = (int)(done * 10 / total);
                    Console.WriteLine($"[AppUpdate] ⬇️ {lastTenth * 10}% — {done / 1024 / 1024}/{total / 1024 / 1024} MB");
                }
            }
            await output.FlushAsync();

            if (total > 0 && done < total)
            {
                Console.WriteLine($"[AppUpdate] ⚠️ Yuklash uzildi ({done / 1024 / 1024}/{total / 1024 / 1024} MB) — keyingi safar davom etadi");
                return false;
            }
            Console.WriteLine("[AppUpdate] ✅ Yuklab olindi");
            return true;
        }
        catch (Exception ex)
        {
            long now = File.Exists(part) ? new FileInfo(part).Length : 0;
            Console.WriteLine($"[AppUpdate] ⚠️ Yuklash uzildi ({now / 1024 / 1024} MB saqlandi, keyin davom etadi): {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> WaitUntilIdleAsync(TimeSpan max)
    {
        var until = max == Timeout.InfiniteTimeSpan ? DateTime.MaxValue : DateTime.UtcNow + max;
        while (DateTime.UtcNow < until)
        {
            bool idle;
            try { idle = IsIdle?.Invoke() ?? true; } catch { idle = false; }
            if (idle) return true;
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
        return false;
    }

    private static bool VerifyApk(string path, out string reason)
    {
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            var pm = ctx.PackageManager!;
#pragma warning disable CA1422, CS0618
            var info = pm.GetPackageArchiveInfo(path, (PackageInfoFlags)0);
            if (info == null) { reason = "APK o'qilmadi (buzilgan)"; return false; }
            if (info.PackageName != ctx.PackageName) { reason = $"boshqa ilova: {info.PackageName}"; return false; }
            var current = pm.GetPackageInfo(ctx.PackageName!, (PackageInfoFlags)0)!;
            long newCode = OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode;
            long curCode = OperatingSystem.IsAndroidVersionAtLeast(28) ? current.LongVersionCode : current.VersionCode;
#pragma warning restore CA1422, CS0618
            if (newCode <= curCode) { reason = $"versiya yangi emas ({newCode} <= {curCode})"; return false; }
            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
#else
        reason = "";
        return true;
#endif
    }

    private static string Sha256Of(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

#if ANDROID
    /// <summary>PackageInstaller.Session — device owner bo'lsa foydalanuvchidan so'ramasdan o'rnatadi.</summary>
    private static void InstallApkSilently(string apkPath)
    {
        var context = Android.App.Application.Context;
        var packageInstaller = context.PackageManager!.PackageInstaller;

        // Avvalgi tugallanmagan sessiyalarni tozalash
        try
        {
            foreach (var info in packageInstaller.MySessions)
                packageInstaller.AbandonSession(info.SessionId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ⚠️ Session tozalash: {ex.Message}");
        }

        var sessionParams = new Android.Content.PM.PackageInstaller.SessionParams(PackageInstallMode.FullInstall);

        // testOnly APK larni ham o'rnatish uchun INSTALL_ALLOW_TEST (0x4)
        try
        {
            var cls = Java.Lang.Class.ForName("android.content.pm.PackageInstaller$SessionParams");
            var field = cls.GetDeclaredField("installFlags");
            field.Accessible = true;
            field.SetInt(sessionParams, field.GetInt(sessionParams) | 0x00000004);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ⚠️ installFlags: {ex.Message}");
        }

        var sessionId = packageInstaller.CreateSession(sessionParams);
        var session = packageInstaller.OpenSession(sessionId);
        try
        {
            using (var fs = File.OpenRead(apkPath))
            {
                var outputStream = session.OpenWrite("base.apk", 0, fs.Length);
                try
                {
                    fs.CopyTo(outputStream);
                    session.Fsync(outputStream);
                }
                finally
                {
                    outputStream.Close();
                    outputStream.Dispose();
                }
            }

            var intent = new Intent("uz.taroziklass.PACKAGE_INSTALLED");
            intent.SetPackage(context.PackageName);
            var flags = OperatingSystem.IsAndroidVersionAtLeast(31)
                ? Android.App.PendingIntentFlags.Mutable
                : Android.App.PendingIntentFlags.UpdateCurrent;
            using var pendingIntent = Android.App.PendingIntent.GetBroadcast(context, sessionId, intent, flags)!;
            session.Commit(pendingIntent.IntentSender);
            Console.WriteLine("[AppUpdate] ✅ Commit yuborildi — o'rnatilmoqda...");
        }
        catch
        {
            session.Abandon();
            throw;
        }
        finally
        {
            session.Dispose();
        }
    }
#endif

    /// <summary>"v2.1.0" → 20100</summary>
    private static long ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return 0;
        var parts = tag.TrimStart('v', 'V').Trim().Split('.');
        long major = parts.Length > 0 && long.TryParse(parts[0], out var a) ? a : 0;
        long minor = parts.Length > 1 && long.TryParse(parts[1], out var b) ? b : 0;
        long patch = parts.Length > 2 && long.TryParse(parts[2], out var c) ? c : 0;
        return major * 10000 + minor * 100 + patch;
    }
}

#if ANDROID
[BroadcastReceiver(Name = "uz.taroziklass.PackageInstallReceiver", Enabled = true, Exported = true)]
public class PackageInstallReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;
        var status = intent.GetIntExtra(Android.Content.PM.PackageInstaller.ExtraStatus, -999);
        var message = intent.GetStringExtra(Android.Content.PM.PackageInstaller.ExtraStatusMessage);
        Console.WriteLine($"[AppUpdate] 🔔 O'rnatish natijasi: status={status}, message={message}");

        if (status == -1)
        {
            // Device owner bo'lmasa tizim tasdiq so'raydi (STATUS_PENDING_USER_ACTION): tasdiqlash oynasini ochamiz
#pragma warning disable CA1422, CS0618
            var confirm = intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
#pragma warning restore CA1422, CS0618
            if (confirm != null)
            {
                confirm.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(confirm);
            }
        }
        else if (status != 0)
        {
            AppUpdateService.ReportInstallFailure(message);
        }
    }
}
#endif
