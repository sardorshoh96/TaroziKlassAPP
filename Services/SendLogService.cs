using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TaroziAPP.Models;
using TaroziAPP.Services.Api;

namespace TaroziAPP.Services
{
    /// <summary>
    /// Service for sending logs to server periodically (every 16 seconds)
    /// </summary>
    public sealed class SendLogService : IDisposable
    {
        private readonly JsonRpcClient _client;
        private readonly DeviceService _deviceService;
        private readonly CredentialStorageService _credentialStorage;
        private readonly DeviceStorageService _deviceStorage;
        private readonly LogsProvider _logsProvider;
        private Timer? _timer;
        private Timer? _refreshTimer; // Har 2 daqiqada yangilash
        private bool _disposed = false;
        private Action<DeviceInfoDto>? _onDeviceRefreshed;

        public SendLogService(JsonRpcClient client, DeviceService deviceService, CredentialStorageService credentialStorage, DeviceStorageService deviceStorage, LogsProvider logsProvider)
        {
            _client = client;
            _deviceService = deviceService;
            _credentialStorage = credentialStorage;
            _deviceStorage = deviceStorage;
            _logsProvider = logsProvider;
            
            // Log yuborish timeri — har 16 soniyada
            _timer = new Timer(async _ => await SendLogsAsync(), null, TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(16));

            // Mustaqil refresh timeri — birinchi marta 5 soniyada, keyin har 2 daqiqada
            // (ilova ochilganda eski cache emas, server dan yangi Payments olinadi)
            _refreshTimer = new Timer(async _ => await PeriodicRefreshAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2));
        }

        public void AddLog(Logs log)
        {
            _logsProvider.AddLog(log);
        }

        public void SetOnDeviceRefreshed(Action<DeviceInfoDto> onDeviceRefreshed)
        {
            _onDeviceRefreshed = onDeviceRefreshed;
        }

        private async Task SendLogsAsync()
        {
            try
            {
                var credentials = await _credentialStorage.RetrieveAsync();
                if (credentials == null)
                {
                    System.Diagnostics.Debug.WriteLine("[SendLogService] ⚠️ Credentials topilmadi, loglar yuborilmadi");
                    return;
                }

                var logsToSend = _logsProvider.GetLogs();
                if (logsToSend.Count == 0) return;

                // Get device ID
                var deviceInfo = await _deviceService.LoginAsync(credentials.Login, credentials.Password);
                if (!deviceInfo.IsSuccess || deviceInfo.Data == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SendLogService] ⚠️ Device info olinmadi: {deviceInfo.ErrorMessage ?? "Unknown error"}");
                    return;
                }
                
                var deviceId = deviceInfo.Data.Id;
                if (string.IsNullOrEmpty(deviceId))
                {
                    System.Diagnostics.Debug.WriteLine("[SendLogService] ⚠️ DeviceId null yoki bo'sh, loglar yuborilmadi");
                    return;
                }

                // Prepare request
                var request = new JsonRpcRequest("OrderLogs", new
                {
                    deviceId,
                    logs = logsToSend.Select(log => new
                    {
                        paymentId = log.PaymentId,
                        productId = log.ProductId,
                        time = log.Time,
                        qty = log.Qty,
                        price = log.Price,
                        discount = log.Discount,
                        totalPrice = log.TotalPrice,
                        status = log.Status
                    }).ToList()
                });

                var result = await _client.PostAsync<JsonElement>(request, credentials.Login, credentials.Password);

                if (result.IsSuccess && result.Data.ValueKind == JsonValueKind.Object)
                {
                    // Clear logs after successful send
                    _logsProvider.ClearLogs();

                    // Check if server returns updated=1, then refresh device info (same as Flutter)
                    bool isUpdated = false;
                    foreach (var prop in result.Data.EnumerateObject())
                    {
                        if (prop.Name.Equals("updated", StringComparison.OrdinalIgnoreCase))
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int num) && num == 1)
                                isUpdated = true;
                            else if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() == "1")
                                isUpdated = true;
                            else if (prop.Value.ValueKind == JsonValueKind.True)
                                isUpdated = true;
                            break;
                        }
                    }

                    if (isUpdated)
                    {
                        // Refresh device info (same as Flutter: authService.refresh())
                        var refreshResult = await _deviceService.RefreshAsync(credentials);
                        if (refreshResult.IsSuccess && refreshResult.Data != null)
                        {
                            // Convert and save device
                            var device = new Models.Device
                            {
                                Id = refreshResult.Data.Id ?? "",
                                Name = refreshResult.Data.Name ?? "",
                                PhoneNumber = refreshResult.Data.ServicePhoneNumber ?? "",
                                Password = refreshResult.Data.Password ?? "",  // JSON dan kelgan nastroyka paroli
                                Payments = refreshResult.Data.Payments?.Select(p => new Payment
                                {
                                    Id = p.Id ?? "",
                                    MerchantId = p.MerchantId ?? "",
                                    ServiceId = p.ServiceId ?? "",
                                    Allow = p.Allow,
                                    Type = p.Type != null ? new PaymentType
                                    {
                                        Name = p.Type.Name ?? "",
                                        Photo = p.Type.Photo ?? ""
                                    } : null
                                }).ToList() ?? new List<Payment>()
                            };
                            await _deviceStorage.SaveAsync(device);
                            
                            // Notify that device was refreshed
                            _onDeviceRefreshed?.Invoke(refreshResult.Data);
                            
                            // Mark device as updated (same as Flutter: authService.updated())
                            Console.WriteLine("[SendLogService] ℹ️ Marking device as updated (updated = 2)...");
                            await _deviceService.UpdatedAsync(credentials, refreshResult.Data.Id);
                        }
                        else
                        {
                            Console.WriteLine($"[SendLogService] ⚠️ Device refresh failed: {refreshResult.ErrorMessage}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[SendLogService] ℹ️ No update needed (updated was not 1).");
                    }
                }
                else
                {
                    Console.WriteLine($"[SendLogService] ⚠️ PostAsync failed: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SendLogService] ❌ Error sending logs: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[SendLogService] ❌ Inner Exception: {ex.InnerException.Message}");
                }
            }
        }

        /// <summary>
        /// Har 2 daqiqada serverni tekshiradi — log bo'lmasa ham yangi ma'lumotlarni yuklaydi.
        /// Basic auth (login/password) orqali RefreshAsync chaqiriladi.
        /// </summary>
        private async Task PeriodicRefreshAsync()
        {
            try
            {
                var credentials = await _credentialStorage.RetrieveAsync();
                if (credentials == null)
                {
                    Console.WriteLine("[SendLogService] ⏱️ PeriodicRefresh: credentials topilmadi");
                    return;
                }

                Console.WriteLine("[SendLogService] ⏱️ PeriodicRefresh: server tekshirilmoqda...");

                var refreshResult = await _deviceService.RefreshAsync(credentials);

                if (refreshResult.IsSuccess && refreshResult.Data != null)
                {
                    var dto = refreshResult.Data;

                    // Yangi device ma'lumotlarini saqlash
                    var device = new Models.Device
                    {
                        Id = dto.Id ?? "",
                        Name = dto.Name ?? "",
                        PhoneNumber = dto.ServicePhoneNumber ?? "",
                        Password = dto.Password ?? "",  // JSON dan kelgan nastroyka paroli
                        Payments = dto.Payments?.Select(p => new Payment
                        {
                            Id = p.Id ?? "",
                            MerchantId = p.MerchantId ?? "",
                            ServiceId = p.ServiceId ?? "",
                            Allow = p.Allow,
                            Type = p.Type != null ? new PaymentType
                            {
                                Name = p.Type.Name ?? "",
                                Photo = p.Type.Photo ?? ""
                            } : null
                        }).ToList() ?? new List<Payment>()
                    };

                    await _deviceStorage.SaveAsync(device);

                    // UI ga xabar berish (Payments, Name va boshqalar yangilangan bo'lishi mumkin)
                    _onDeviceRefreshed?.Invoke(dto);

                    Console.WriteLine($"[SendLogService] ✅ PeriodicRefresh: yangilandi. Payments={dto.Payments?.Count ?? 0}");

                    // Serverga "yangilanish qabul qilindi" (updated=2) xabarini yuborish
                    try
                    {
                        await _deviceService.UpdatedAsync(credentials, dto.Id);
                        Console.WriteLine("[SendLogService] ✅ PeriodicRefresh: updated=2 yuborildi");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SendLogService] ⚠️ PeriodicRefresh: UpdatedAsync xatosi: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"[SendLogService] ⚠️ PeriodicRefresh: refresh muvaffaqiyatsiz: {refreshResult.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SendLogService] ❌ PeriodicRefresh xatosi: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer?.Dispose();
                _refreshTimer?.Dispose();
                _disposed = true;
            }
        }
    }
}
