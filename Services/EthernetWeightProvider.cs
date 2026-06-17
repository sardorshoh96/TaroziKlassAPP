using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace TaroziAPP.Services
{
    /// <summary>
    /// Ethernet (TCP/IP) Weight provider acting as a TCP SERVER
    /// Listens on a specified port (default 8234)
    /// </summary>
    public class EthernetWeightProvider : IDisposable
    {
        private TcpListener? _listener;
        private TcpClient? _connectedClient;
        private NetworkStream? _stream;
        private int _port = 8234;
        private bool _disposed = false;
        private bool _isRunning = false;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private readonly object _lockObject = new object();
        private DateTime _lastDataTime = DateTime.MinValue;

        public event Action<int>? WeightChanged;
        public event Action<byte[]>? RawDataReceived;
        public int CurrentWeight { get; private set; }

        public EthernetWeightProvider() { }

        public void Start(int port = 8234)
        {
            lock (_lockObject)
            {
                if (_isRunning && _port == port) return;

                Stop();

                _port = port;
                _isRunning = true;
                _cts = new CancellationTokenSource();

                _listenTask = Task.Run(() => ListenLoop(_cts.Token), _cts.Token);
                System.Diagnostics.Debug.WriteLine($"[EthernetServer] Started listening on port {port}");
            }
        }

        private async Task ListenLoop(CancellationToken token)
        {
            byte[] buffer = new byte[4096];

            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                System.Diagnostics.Debug.WriteLine($"[EthernetServer] TCP Listener started on port {_port}");

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (_connectedClient == null || !_connectedClient.Connected)
                        {
                            System.Diagnostics.Debug.WriteLine("[EthernetServer] Waiting for client connection...");
                            _connectedClient = await _listener.AcceptTcpClientAsync(token);
                            _stream = _connectedClient.GetStream();
                            System.Diagnostics.Debug.WriteLine($"[EthernetServer] Client connected: {((IPEndPoint)_connectedClient.Client.RemoteEndPoint!).Address}");
                        }

                        if (_stream != null && _stream.CanRead)
                        {
                            // We use a shorter timeout for reading to stay responsive to cancellation
                            if (_connectedClient.Available > 0)
                            {
                                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);
                                if (bytesRead > 0)
                                {
                                    _lastDataTime = DateTime.Now;
                                    byte[] data = new byte[bytesRead];
                                    Array.Copy(buffer, 0, data, 0, bytesRead);

                                    MainThread.BeginInvokeOnMainThread(() =>
                                    {
                                        RawDataReceived?.Invoke(data);
                                    });
                                }
                                else
                                {
                                    // Client disconnected
                                    CloseClient();
                                }
                            }
                            else
                            {
                                // Check for clear weight if no data for 3s
                                if (CurrentWeight != 0 && (DateTime.Now - _lastDataTime).TotalSeconds >= 3)
                                {
                                    _lastDataTime = DateTime.Now;
                                    MainThread.BeginInvokeOnMainThread(() => Clear());
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EthernetServer] Error in listen loop: {ex.Message}");
                        CloseClient();
                        await Task.Delay(1000, token);
                    }

                    await Task.Delay(50, token);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EthernetServer] CRITICAL: Could not start listener: {ex.Message}");
            }
            finally
            {
                _listener?.Stop();
            }
        }

        private void CloseClient()
        {
            _stream?.Dispose();
            _stream = null;
            _connectedClient?.Dispose();
            _connectedClient = null;
        }

        public void Stop()
        {
            lock (_lockObject)
            {
                _isRunning = false;
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                CloseClient();
                _listener?.Stop();
                _listener = null;

                _listenTask = null;
            }
        }

        public void ProcessWeightFromDevice(int weightGrams)
        {
            if (weightGrams != CurrentWeight)
            {
                CurrentWeight = weightGrams;
                WeightChanged?.Invoke(weightGrams);
            }
        }

        public void Clear()
        {
            CurrentWeight = 0;
            WeightChanged?.Invoke(0);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
