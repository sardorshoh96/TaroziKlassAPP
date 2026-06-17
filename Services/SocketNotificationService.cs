using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SocketIOClient;

namespace TaroziAPP.Services;

public sealed class SocketNotificationService : IAsyncDisposable
{
    private SocketIOClient.SocketIO? _socket;
    private string? _lastUrl;
    private string? _lastLogin;
    private string? _lastPassword;
    private readonly CredentialStorageService _credentialStorage;
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public SocketNotificationService(CredentialStorageService credentialStorage)
    {
        _credentialStorage = credentialStorage;
    }

    public event Action<bool>? ConnectionStateChanged;
    public event Action<object>? PaymentReceivedResponse;

    public bool IsConnected => _socket?.Connected == true;

    public async Task ConnectAsync(string url, string login, string password)
    {
        _lastUrl = url;
        _lastLogin = login;
        _lastPassword = password;

        await EnsureConnected();
    }

    private async Task EnsureConnected()
    {
        await _semaphore.WaitAsync();

        try
        {
            if (_socket != null && _socket.Connected)
            {
                return;
            }

            if (string.IsNullOrEmpty(_lastUrl) || string.IsNullOrEmpty(_lastLogin) || string.IsNullOrEmpty(_lastPassword))
            {
                var credentials = await _credentialStorage.RetrieveAsync();
                if (credentials != null)
                {
                    _lastLogin = credentials.Login;
                    _lastPassword = credentials.Password;
                    _lastUrl = "https://device.radiomer.uz";
                }
            }

            if (string.IsNullOrEmpty(_lastUrl))
            {
                System.Diagnostics.Debug.WriteLine("[SocketNotificationService] ⚠️ URL yo'q va credentials topilmadi, ulanilmadi.");
                return;
            }

            Console.WriteLine("[SocketNotificationService] 🔄 Connecting...");

            // Dispose old socket
            _socket?.Dispose();

            // Create new Socket.IO client
            _socket = new SocketIOClient.SocketIO(
                _lastUrl,
                new SocketIOClient.SocketIOOptions
                {
                    Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
                    Query = new[]
                    {
                        new KeyValuePair<string, string>("userName", _lastLogin ?? ""),
                        new KeyValuePair<string, string>("password", _lastPassword ?? "")
                    },
                    Reconnection = false 
                });

            // Setup event handlers
            _socket.OnConnected += (_, _) =>
            {
                Console.WriteLine("[SocketNotificationService] ✅ Connected");
                ConnectionStateChanged?.Invoke(true);
            };

            _socket.OnDisconnected += (_, reason) =>
            {
                Console.WriteLine($"[SocketNotificationService] ❌ Disconnected: {reason}");
                ConnectionStateChanged?.Invoke(false);
            };

            // SERVER → ping
            _socket.On("ping", async _ =>
            {
                try
                {
                    Console.WriteLine("[SocketNotificationService] ❤️ ping received → pong sent");
                    if (_socket != null && _socket.Connected)
                    {
                        await _socket.EmitAsync("pong");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SocketNotificationService] Error sending pong: {ex.Message}");
                }
            });

            // SERVER → pong (agar bo'lsa)
            _socket.On("pong", _ =>
            {
                Console.WriteLine("[SocketNotificationService] 💚 pong received");
            });

            // Listen for paymentReceivedResponse event
            _socket.On("paymentReceivedResponse", response =>
            {
                try
                {
                    var data = response.GetValue<object>();
                    PaymentReceivedResponse?.Invoke(data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SocketNotificationService] Error parsing paymentReceivedResponse: {ex.Message}");
                }
            });

            await _socket.ConnectAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SocketNotificationService] Connect error: {ex.Message}");
            _socket?.Dispose();
            _socket = null;
        }
        finally
        {
            _semaphore.Release();
        }
    }


    public async Task SendPaymentReceivedAsync(string deviceId, string deviceName, string paymentId, string paymentName, string amount)
    {
        try
        {
            int maxRetries = 3;
            bool connected = false;
            
            for (int retry = 0; retry < maxRetries; retry++)
            {
                if (_socket == null || !_socket.Connected)
                {
                    if (retry > 0)
                    {
                        await Task.Delay(1000 * retry);
                    }
                    
                    await EnsureConnected();
                    
                    int waitCount = 0;
                    while (_socket != null && !_socket.Connected && waitCount < 25)
                    {
                        await Task.Delay(200);
                        waitCount++;
                    }
                }
                
                if (_socket != null && _socket.Connected)
                {
                    connected = true;
                    break;
                }
            }
            
            if (!connected || _socket == null)
            {
                Console.WriteLine("[SocketNotificationService] ❌ Socket ulanmadi! PaymentReceived yuborilmadi.");
                return;
            }

            var data = new
            {
                deviceId = deviceId,
                deviceName = deviceName,
                paymentId = paymentId,
                paymentName = paymentName,
                amount = amount
            };

            Console.WriteLine($"[SocketNotificationService] 📤 Sending paymentReceived: DeviceId={deviceId}, Amount={amount}");
            await _socket.EmitAsync("paymentReceived", data);
            Console.WriteLine("[SocketNotificationService] ✅ paymentReceived yuborildi");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SocketNotificationService] Error sending paymentReceived: {ex.Message}");
            
            // Xatolik bo'lsa, socket'ni yopish
            if (_socket != null)
            {
                try
                {
                    if (_socket.Connected)
                    {
                        await _socket.DisconnectAsync();
                    }
                }
                catch
                {
                    // Ignore disconnect errors
                }
                _socket?.Dispose();
                _socket = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket != null)
        {
            try
            {
                if (_socket.Connected)
                {
                    await _socket.DisconnectAsync();
                }
            }
            catch
            {
                // Ignore disconnect errors
            }
            _socket?.Dispose();
            _socket = null;
        }
        _semaphore.Dispose();
    }
}

