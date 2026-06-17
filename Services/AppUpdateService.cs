using System.Text.Json;
using System.Text.Json.Serialization;

#if ANDROID
using Android.Content;
using Android.OS;
// FileProvider to'liq nom bilan — Microsoft.Maui.Storage.FileProvider bilan adashtirmaslik uchun
using AndroidXFileProvider = AndroidX.Core.Content.FileProvider;
#endif

namespace TaroziAPP.Services;

/// <summary>
/// GitHub Releases dan yangi APK versiyasini tekshirib, yuklab, o'rnatadi.
/// </summary>
public class AppUpdateService : IDisposable
{
    // ⚙️ BU JOYNI O'ZGARTIRING: sizning GitHub repo URL
    private const string GitHubOwner = "sardorshoh96";
    private const string GitHubRepo  = "TaroziKlassAPP";

    private const string GitHubApiUrl =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private readonly HttpClient _httpClient;
    private Timer? _updateTimer;
    private bool _disposed;

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
        Console.WriteLine("[AppUpdate] ⏰ Periodic yangilanish tekshiruvi boshlandi (har 1 soat)");
        _updateTimer = new Timer(
            async _ => await CheckAndUpdateAsync(),
            null,
            TimeSpan.FromSeconds(10),   // birinchi tekshiruv
            TimeSpan.FromHours(1));      // keyin har 1 soat
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _updateTimer?.Dispose();
        _httpClient.Dispose();
    }

    /// <summary>
    /// Yangi versiya borligini tekshiradi va bo'lsa o'rnatadi.
    /// </summary>
    public async Task CheckAndUpdateAsync(bool showNoUpdateMessage = false)
    {
        try
        {
            Console.WriteLine("[AppUpdate] 🔍 Yangi versiya tekshirilmoqda...");

            // GitHub releases API dan ma'lumot olish
            var json = await _httpClient.GetStringAsync(GitHubApiUrl);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release == null)
            {
                Console.WriteLine("[AppUpdate] ⚠️ Release ma'lumotlari topilmadi");
                return;
            }

            // Versiyani solishtirish
            var remoteVersion = ParseVersion(release.TagName);
            var localVersion  = AppInfo.Version.Major * 10000
                              + AppInfo.Version.Minor * 100
                              + AppInfo.Version.Build;

            Console.WriteLine($"[AppUpdate] Hozirgi: {localVersion}, GitHub: {remoteVersion} (tag={release.TagName})");

            if (remoteVersion <= localVersion)
            {
                Console.WriteLine("[AppUpdate] ✅ Ilova eng yangi versiyada");
                if (showNoUpdateMessage)
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        Application.Current?.MainPage?.DisplayAlert(
                            "Yangilanish", "Ilova eng yangi versiyada.", "OK"));
                return;
            }

            // APK asset topish
            var apkAsset = release.Assets?.FirstOrDefault(a =>
                a.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));

            if (apkAsset == null)
            {
                Console.WriteLine("[AppUpdate] ⚠️ Release da APK topilmadi");
                return;
            }

            Console.WriteLine($"[AppUpdate] 🆕 Yangi versiya: {release.TagName} — {apkAsset.Name}");

            // Foydalanuvchiga so'rash
            bool confirm = false;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                confirm = await (Application.Current?.MainPage?.DisplayAlert(
                    "Yangi versiya mavjud! 🆕",
                    $"Versiya: {release.TagName}\n\nYangilanishni yuklab o'rnatilsinmi?",
                    "Ha, o'rnat", "Keyinroq") ?? Task.FromResult(false));
            });

            if (!confirm) return;

            // APK yuklab olish
            await DownloadAndInstallAsync(apkAsset.DownloadUrl, apkAsset.Name);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[AppUpdate] ❌ Tarmoq xatosi: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ❌ Xato: {ex.Message}");
        }
    }

    private async Task DownloadAndInstallAsync(string downloadUrl, string fileName)
    {
        try
        {
            Console.WriteLine($"[AppUpdate] ⬇️ Yuklanmoqda: {downloadUrl}");

            // Saqlash papkasi
            var cacheDir = Path.Combine(FileSystem.CacheDirectory, "updates");
            Directory.CreateDirectory(cacheDir);
            var apkPath = Path.Combine(cacheDir, fileName);

            // Eski faylni o'chirish
            if (File.Exists(apkPath)) File.Delete(apkPath);

            // Yuklab olish (progress ko'rsatiladi)
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0L;
            using var stream    = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(apkPath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int  read;

            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                downloaded += read;

                if (totalBytes > 0)
                {
                    var percent = (int)(downloaded * 100 / totalBytes);
                    Console.WriteLine($"[AppUpdate] ⬇️ {percent}% ({downloaded}/{totalBytes} bytes)");
                }
            }

            fileStream.Close();
            Console.WriteLine($"[AppUpdate] ✅ Yuklab olindi: {apkPath}");

            // O'rnatish
            await InstallApkAsync(apkPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ❌ Yuklash xatosi: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(() =>
                Application.Current?.MainPage?.DisplayAlert(
                    "Xato", $"APK yuklab bo'lmadi: {ex.Message}", "OK"));
        }
    }

    private Task InstallApkAsync(string apkPath)
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var apkFile = new Java.IO.File(apkPath);
            var apkUri  = AndroidXFileProvider.GetUriForFile(
                context,
                "uz.taroziklass.fileprovider",
                apkFile);

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(apkUri, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            intent.AddFlags(ActivityFlags.NewTask);

            context.StartActivity(intent);
            Console.WriteLine("[AppUpdate] ✅ O'rnatuvchi ishga tushirildi");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppUpdate] ❌ O'rnatish xatosi: {ex.Message}");
        }
#endif
        return Task.CompletedTask;
    }

    /// <summary>
    /// "v2.1.0" → 20100, "v2" → 20000, "3" → 30000 formatga o'tkazadi
    /// </summary>
    private static long ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return 0;

        // "v" prefixini olib tashlash
        var clean = tag.TrimStart('v', 'V').Trim();
        var parts = clean.Split('.');

        long major = parts.Length > 0 && long.TryParse(parts[0], out var maj) ? maj : 0;
        long minor = parts.Length > 1 && long.TryParse(parts[1], out var min) ? min : 0;
        long patch = parts.Length > 2 && long.TryParse(parts[2], out var pat) ? pat : 0;

        return major * 10000 + minor * 100 + patch;
    }

    // GitHub API response modellari
    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
