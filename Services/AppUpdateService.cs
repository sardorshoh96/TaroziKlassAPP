#if ANDROID
using Android.Content;
using Android.OS;
#endif

namespace TaroziAPP.Services;

/// <summary>
/// GitHub Releases dan yangi APK versiyasini tekshirib, yuklab, jimgina o'rnatadi.
/// </summary>
public class AppUpdateService : IDisposable
{
    // ⚙️ GitHub repo
    private const string GitHubOwner = "sardorshoh96";
    private const string GitHubRepo  = "TaroziKlassAPP";

    // APK fayl nomi — release da shu nom bilan yuklanadi
    private const string ApkFileName = "uz.taroziklass-Signed.apk";

    // redirect URL — rate limit yo'q, API token kerak emas
    private const string GitHubLatestUrl =
        $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private readonly HttpClient _httpClient;
    private Timer? _updateTimer;
    private bool _disposed;
    private bool _isChecking;   // parallel tekshiruvni bloklash
    private bool _isUpdating;   // yuklash boshlanganda yangi dialogni bloklash

#if ANDROID
    private string? _pendingApkPath;
#endif

    public AppUpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TaroziAPP-Updater");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Ishga tushganda 10 soniyada birinchi tekshiruv, keyin har 1 soatda.
    /// </summary>
    public void StartPeriodicCheck()
    {
        Console.WriteLine("[AppUpdate] ⏰ Periodic yangilanish tekshiruvi boshlandi (har 1 soatda)");
        _updateTimer = new Timer(
            async _ => await CheckAndUpdateAsync(),
            null,
            TimeSpan.FromSeconds(10),  // birinchi tekshiruv 10 soniyadan so'ng
            TimeSpan.FromHours(1));    // har 1 soatda
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _updateTimer?.Dispose();
        _httpClient.Dispose();
    }

    /// <summary>
    /// Yangi versiya borligini tekshiradi va bo'lsa jimgina o'rnatadi.
    /// GitHub redirect URL usuli — API rate limit yo'q.
    /// </summary>
    public async Task CheckAndUpdateAsync(bool showNoUpdateMessage = false)
    {
        // Parallel tekshiruvni bloklash
        if (_isChecking || _isUpdating) return;
        _isChecking = true;

        try
        {
            Console.WriteLine("[AppUpdate] 🔍 Yangi versiya tekshirilmoqda...");

            var tagName = await GetLatestTagViaRedirectAsync();

            if (tagName == null)
            {
                Console.WriteLine("[AppUpdate] ⚠️ Versiya ma'lumoti olinmadi");
                return;
            }

            // Versiyani solishtirish
            var remoteVersion = ParseVersion(tagName);
            var localVersion  = AppInfo.Version.Major * 10000
                              + AppInfo.Version.Minor * 100
                              + AppInfo.Version.Build;

            Console.WriteLine($"[AppUpdate] Hozirgi: {localVersion}, GitHub: {remoteVersion} (tag={tagName})");

            if (remoteVersion <= localVersion)
            {
                Console.WriteLine("[AppUpdate] ✅ Ilova eng yangi versiyada");
                if (showNoUpdateMessage)
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        Application.Current?.MainPage?.DisplayAlert(
                            "Yangilanish", "Ilova eng yangi versiyada.", "OK"));
                return;
            }

            // APK download URL ni to'g'ridan-to'g'ri quramiz — API kerak emas
            var downloadUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{tagName}/{ApkFileName}";

            Console.WriteLine($"[AppUpdate] 🆕 Yangi versiya: {tagName}");
            Console.WriteLine($"[AppUpdate] ⬇️ URL: {downloadUrl}");

            // Foydalanuvchiga so'rash (faqat qo'lda tekshirilganda, aks holda fonda jimgina yuklanadi)
            bool confirm = true;
            if (showNoUpdateMessage)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    confirm = await (Application.Current?.MainPage?.DisplayAlert(
                        "Yangi versiya mavjud! 🆕",
                        $"Versiya: {tagName}\n\nYangilanishni yuklab o'rnatilsinmi?",
                        "Ha, o'rnat", "Keyinroq") ?? Task.FromResult(false));
                });
            }

            if (!confirm) return;

            // Update boshlandi — boshqa dialoglar chiqmasin, timer to'xtatilsin
            _isUpdating = true;
            _updateTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // APK yuklab o'rnatish
            await DownloadAndInstallAsync(downloadUrl, ApkFileName);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[AppUpdate] ❌ Tarmoq xatosi: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ❌ Xato: {ex.Message}");
        }
        finally
        {
            _isChecking = false;
        }
    }

    /// <summary>
    /// GitHub releases/latest sahifasining redirect URL sidan tag nomini o'qiydi.
    /// Bu usul API rate limit ga tushirmaydi — autentifikatsiya kerak emas.
    /// </summary>
    private async Task<string?> GetLatestTagViaRedirectAsync()
    {
        try
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "TaroziAPP-Updater");
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await client.GetAsync(GitHubLatestUrl);

            // 302 redirect — Location header da tag nomi bor: /releases/tag/v1.0.1
            if (response.StatusCode == System.Net.HttpStatusCode.Found ||
                response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
            {
                var location = response.Headers.Location?.ToString();
                Console.WriteLine($"[AppUpdate] Redirect: {location}");

                if (location != null)
                {
                    var tagIdx = location.LastIndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase);
                    if (tagIdx >= 0)
                        return location[(tagIdx + "/releases/tag/".Length)..];
                }
            }

            Console.WriteLine($"[AppUpdate] ⚠️ Kutilmagan status: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ❌ Redirect xatosi: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// APK ni yuklab oladi va o'rnatadi.
    /// </summary>
    private async Task DownloadAndInstallAsync(string downloadUrl, string fileName)
    {
        try
        {
            Console.WriteLine($"[AppUpdate] ⬇️ Yuklanmoqda: {downloadUrl}");

            var cacheDir = Path.Combine(FileSystem.CacheDirectory, "updates");
            Directory.CreateDirectory(cacheDir);
            var apkPath = Path.Combine(cacheDir, fileName);

            if (File.Exists(apkPath)) File.Delete(apkPath);

            // Download uchun alohida HttpClient — timeout yo'q (katta fayl uchun)
            using var downloadClient = new HttpClient();
            downloadClient.DefaultRequestHeaders.Add("User-Agent", "TaroziAPP-Updater");
            downloadClient.Timeout = Timeout.InfiniteTimeSpan;

            using var response = await downloadClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0L;
            var totalMB    = totalBytes > 0 ? $"{totalBytes / 1024 / 1024} MB" : "?";
            Console.WriteLine($"[AppUpdate] 📦 Fayl hajmi: {totalMB}");

            using var stream     = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(apkPath);

            var buffer      = new byte[81920];
            long downloaded = 0;
            int  read;
            int  lastPercent = -1;

            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                downloaded += read;

                if (totalBytes > 0)
                {
                    var percent = (int)(downloaded * 100 / totalBytes);
                    if (percent / 10 != lastPercent / 10)
                    {
                        lastPercent = percent;
                        Console.WriteLine($"[AppUpdate] ⬇️ {percent}% — {downloaded / 1024 / 1024}/{totalMB}");
                    }
                }
            }

            fileStream.Close();
            Console.WriteLine($"[AppUpdate] ✅ Yuklab olindi: {apkPath}");

            await InstallApkAsync(apkPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ❌ Yuklash xatosi: {ex.Message}");
            _isUpdating = false;
            _updateTimer?.Change(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
            await MainThread.InvokeOnMainThreadAsync(() =>
                Application.Current?.MainPage?.DisplayAlert(
                    "Xato", $"APK yuklab bo'lmadi: {ex.Message}", "OK"));
        }
    }

    /// <summary>
    /// PackageInstaller API orqali jimgina o'rnatadi — foydalanuvchi harakatsiz.
    /// </summary>
    private async Task InstallApkAsync(string apkPath)
    {
#if ANDROID
        try
        {
            Console.WriteLine("[AppUpdate] 📦 PackageInstaller orqali jimgina o'rnatilmoqda...");
            await InstallApkSilentlyAsync(apkPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ❌ O'rnatish xatosi: {ex.Message}");
        }
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    /// <summary>
    /// PackageInstaller.Session — foydalanuvchidan so'ramasdan APK o'rnatadi.
    /// </summary>
    private async Task InstallApkSilentlyAsync(string apkPath)
    {
        var context          = Android.App.Application.Context;
        var packageInstaller = context.PackageManager!.PackageInstaller;

        // Avvalgi muvaffaqiyatsiz sessionlarni tozalaymiz
        try
        {
            var mySessions = packageInstaller.MySessions;
            foreach (var info in mySessions)
            {
                Console.WriteLine($"[AppUpdate] 🗑️ Eski session tozalanmoqda: {info.SessionId}");
                packageInstaller.AbandonSession(info.SessionId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ⚠️ Session tozalash: {ex.Message}");
        }

        var sessionParams = new Android.Content.PM.PackageInstaller.SessionParams(
            Android.Content.PM.PackageInstallMode.FullInstall);

        // testOnly APK'larni ham o'rnatishga ruxsat berish uchun installFlags'ga INSTALL_ALLOW_TEST (0x00000004) flagini qo'shamiz
        try
        {
            var sessionParamsClass = Java.Lang.Class.ForName("android.content.pm.PackageInstaller$SessionParams");
            var installFlagsField = sessionParamsClass.GetDeclaredField("installFlags");
            installFlagsField.Accessible = true;
            int flags = installFlagsField.GetInt(sessionParams);
            flags |= 0x00000004; // INSTALL_ALLOW_TEST
            installFlagsField.SetInt(sessionParams, flags);
            Console.WriteLine("[AppUpdate] 🔧 SessionParams: testOnly APK o'rnatish ruxsati berildi (0x00000004)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ⚠️ SessionParams installFlags o'rnatib bo'lmadi: {ex.Message}");
        }

        // SetAppPackageName ba'zi Android versiyalarida "Files still open" beradi — olib tashlandi

        var sessionId = packageInstaller.CreateSession(sessionParams);
        Console.WriteLine($"[AppUpdate] 🔧 Session yaratildi: {sessionId}");

        // session ni using var emas — Commit() dan KEYIN dispose qilamiz
        var session = packageInstaller.OpenSession(sessionId);
        try
        {
            using (var fs = File.OpenRead(apkPath))
            {
                var outputStream = session.OpenWrite("base.apk", 0, fs.Length);
                try
                {
                    Console.WriteLine("[AppUpdate] 📂 APK session ga yozilmoqda...");
                    fs.CopyTo(outputStream);
                    session.Fsync(outputStream);
                }
                finally
                {
                    outputStream.Close();
                    outputStream.Dispose();
                }
            }

            Console.WriteLine("[AppUpdate] ✅ APK session ga yozildi");

            var intent = new Intent("uz.taroziklass.PACKAGE_INSTALLED");
            intent.SetPackage(context.PackageName);

            var flags = Build.VERSION.SdkInt >= BuildVersionCodes.S
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

    /// <summary>
    /// Agar pending APK bo'lsa (ruxsat keyin berilsa), o'rnatishni qayta urinadi.
    /// </summary>
    public void TryInstallPendingApk()
    {
        if (_pendingApkPath == null) return;
        var path = _pendingApkPath;
        _pendingApkPath = null;
        Console.WriteLine("[AppUpdate] 🔄 Pending APK o'rnatilmoqda...");
        _ = InstallApkSilentlyAsync(path);
    }
#endif

    /// <summary>
    /// "v2.1.0" → 20100, "v1.0.0" → 10000 formatga o'tkazadi
    /// </summary>
    private static long ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return 0;

        var clean = tag.TrimStart('v', 'V').Trim();
        var parts = clean.Split('.');

        long major = parts.Length > 0 && long.TryParse(parts[0], out var maj) ? maj : 0;
        long minor = parts.Length > 1 && long.TryParse(parts[1], out var min) ? min : 0;
        long patch = parts.Length > 2 && long.TryParse(parts[2], out var pat) ? pat : 0;

        return major * 10000 + minor * 100 + patch;
    }
}

#if ANDROID
[BroadcastReceiver(Name = "uz.taroziklass.PackageInstallReceiver", Enabled = true, Exported = true)]
public class PackageInstallReceiver : BroadcastReceiver
{
    public override void OnReceive(Context context, Intent intent)
    {
        var status = intent.GetIntExtra(Android.Content.PM.PackageInstaller.ExtraStatus, -999);
        var message = intent.GetStringExtra(Android.Content.PM.PackageInstaller.ExtraStatusMessage);
        Console.WriteLine($"[AppUpdate] 🔔 O'rnatish natijasi: status={status}, message={message}");
    }
}
#endif
