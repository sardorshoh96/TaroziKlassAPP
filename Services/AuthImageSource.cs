using System.Text;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Maui.Storage;

namespace TaroziAPP.Services
{
    /// <summary>
    /// Helper class to load images with Basic Auth and caching
    /// </summary>
    public static class AuthImageLoader
    {
        private static readonly string CacheDirectory = Path.Combine(FileSystem.CacheDirectory, "payment_images");
        
        static AuthImageLoader()
        {
            // Ensure cache directory exists
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }
        
        /// <summary>
        /// Gets cache file path for an image URL
        /// </summary>
        private static string GetCacheFilePath(string url)
        {
            // Create hash from URL for cache key
            var urlBytes = Encoding.UTF8.GetBytes(url);
            var hashBytes = SHA256.HashData(urlBytes);
            var hashString = Convert.ToHexString(hashBytes);
            return Path.Combine(CacheDirectory, $"{hashString}.cache");
        }
        
        /// <summary>
        /// Loads image from cache or downloads and caches it
        /// </summary>
        public static async Task<ImageSource?> LoadImageAsync(string url, string username, string password)
        {
            try
            {
                var cacheFilePath = GetCacheFilePath(url);
                
                // Try to load from cache first
                if (File.Exists(cacheFilePath))
                {
                    try
                    {
                        var cachedBytes = await File.ReadAllBytesAsync(cacheFilePath);
                        if (cachedBytes.Length > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AuthImageLoader] Loaded from cache: {url}");
                            return ImageSource.FromStream(() => new MemoryStream(cachedBytes));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AuthImageLoader] Cache read error: {ex.Message}");
                        // If cache read fails, continue to download
                    }
                }
                
                // Download image if not in cache
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                // Create Basic Auth header
                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
                
                System.Diagnostics.Debug.WriteLine($"[AuthImageLoader] Downloading: {url}");
                System.Diagnostics.Debug.WriteLine($"[AuthImageLoader] Username: '{username}', Password length: {password?.Length ?? 0}");
                
                var response = await httpClient.GetAsync(url);
                System.Diagnostics.Debug.WriteLine($"[AuthImageLoader] Response status: {response.StatusCode}");
                response.EnsureSuccessStatusCode();
                
                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                
                // Save to cache
                try
                {
                    await File.WriteAllBytesAsync(cacheFilePath, imageBytes);
                    System.Diagnostics.Debug.WriteLine($"[AuthImageLoader] Cached image: {url}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AuthImageLoader] Cache write error: {ex.Message}");
                    // Continue even if cache write fails
                }
                
                return ImageSource.FromStream(() => new MemoryStream(imageBytes));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthImageLoader] Error loading image: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Clears all cached images
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    var files = Directory.GetFiles(CacheDirectory);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthImageLoader] Cache clear error: {ex.Message}");
            }
        }
    }
}

