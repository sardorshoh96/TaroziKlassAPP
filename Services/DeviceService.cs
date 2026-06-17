using TaroziAPP.Models;
using TaroziAPP.Services.Api;

namespace TaroziAPP.Services;

public sealed class DeviceService
{
    private readonly JsonRpcClient _client;

    public DeviceService(JsonRpcClient? client = null)
    {
        _client = client ?? new JsonRpcClient("https://device.radiomer.uz/api/device/v1");
    }

    /// <summary>
    /// Sets credentials to be used automatically for all requests
    /// </summary>
    public void SetCredentials(string login, string password)
    {
        _client.SetCredentials(login, password);
    }

    /// <summary>
    /// Clears saved credentials
    /// </summary>
    public void ClearCredentials()
    {
        _client.ClearCredentials();
    }

    public Task<ApiResult<DeviceInfoDto>> LoginAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        var request = new JsonRpcRequest("info");
        return _client.PostAsync<DeviceInfoDto>(request, login, password, cancellationToken);
    }

    public Task<ApiResult<DeviceInfoDto>> RefreshAsync(UserCredentials? credentials = null, CancellationToken cancellationToken = default)
    {
        var request = new JsonRpcRequest("info");
        return _client.PostAsync<DeviceInfoDto>(request, credentials?.Login, credentials?.Password, cancellationToken);
    }

    public Task<ApiResult<System.Text.Json.JsonElement>> MarkDeviceUpdatedAsync(UserCredentials? credentials = null, string? deviceId = null, CancellationToken cancellationToken = default)
    {
        var request = new JsonRpcRequest("updatedDevice", new
        {
            deviceId,
            updated = 2
        });

        return _client.PostAsync<System.Text.Json.JsonElement>(request, credentials?.Login, credentials?.Password, cancellationToken);
    }

    public async Task UpdatedAsync(UserCredentials? credentials = null, string? deviceId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            Console.WriteLine($"[DeviceService] ⏳ UpdatedAsync chaqirilmoqda. DeviceId={deviceId}");
            await MarkDeviceUpdatedAsync(credentials, deviceId, cancellationToken);
            Console.WriteLine($"[DeviceService] ✅ UpdatedAsync muvaffaqiyatli (updated=2 yuborildi)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DeviceService] ❌ UpdatedAsync error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends Telegram notification via server API
    /// Equivalent to Flutter project's server-based Telegram sending
    /// </summary>
    public Task<ApiResult<bool>> SendTelegramNotificationAsync(
        UserCredentials? credentials = null,
        string? deviceId = null,
        string? deviceName = null,
        string? paymentId = null,
        string? paymentName = null,
        string? amount = null,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        var request = new JsonRpcRequest("SendTelegram", new
        {
            deviceId,
            deviceName,
            paymentId,
            paymentName,
            amount,
            message
        });

        return _client.PostAsync<bool>(request, credentials?.Login, credentials?.Password, cancellationToken);
    }
}

