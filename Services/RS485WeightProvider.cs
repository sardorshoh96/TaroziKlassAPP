using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using TaroziAPP.Models;

namespace TaroziAPP.Services
{
    /// <summary>
    /// RS-485 Weight provider for processing weight data from scale devices
    /// Based on TaroziKlass project implementation
    /// Uses independent reading task that NEVER stops
    /// </summary>
    public class RS485WeightProvider : IDisposable
    {
        private const int DefaultBaudRate = 9600;
        
        private int _fd = -1;
        private string _portName = "/dev/ttyS0";
        private int _baudRate = DefaultBaudRate;
        private PaymentState? _paymentState;
        private bool _disposed = false;
        private CancellationTokenSource? _readCancellationTokenSource;
        private Task? _readTask;
        private readonly object _lockObject = new object();
        private DateTime _lastDataTime = DateTime.MinValue;

        public event Action<int>? WeightChanged;
        public event Action<byte[]>? RawDataReceived; // Raw data event for device type processing
        public int CurrentWeight { get; private set; }

        public RS485WeightProvider() { }

        public RS485WeightProvider(PaymentState? paymentState = null)
        {
            _paymentState = paymentState;
        }

        /// <summary>
        /// Starts reading weight data from the RS-485 port
        /// Reading task runs independently and NEVER stops
        /// </summary>
        /// <param name="portName">Serial port name (e.g., "COM2" on Windows or "/dev/ttyS2" on Linux)</param>
        public void Start(string portName = "/dev/ttyS2", int baudRate = 9600, int dataBits = 8, int stopBits = 1, AndroidSerialPort.Parity parity = AndroidSerialPort.Parity.None)
        {
            lock (_lockObject)
            {
                _portName = portName;
                _baudRate = baudRate;
                
                // Close existing port if open
                if (_fd >= 0)
                {
                    try
                    {
                        AndroidSerialPort.CloseFd(_fd);
                    }
                    catch { }
                }
                
                try
                {
                    _fd = AndroidSerialPort.Open(portName, _baudRate, dataBits, stopBits, parity);
                    if (_fd >= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RS485] Port opened: {portName}, fd: {_fd}");
                        
                        // Start reading task (only if not already running)
                        StartReading();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[RS485] Failed to open port {portName}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RS485] Connect error: {ex.Message}");
                    _fd = -1;
                }
            }
        }

        /// <summary>
        /// Starts reading from validator port in a separate independent task
        /// This task runs independently and NEVER stops - it always tries to read
        /// Based on TaroziKlass Connect.ReadData implementation
        /// </summary>
        private void StartReading()
        {
            lock (_lockObject)
            {
                // If task is already running, don't start a new one
                if (_readTask != null && !_readTask.IsCompleted)
                {
                    System.Diagnostics.Debug.WriteLine("[RS485] Reading task already running, skipping start");
                    return;
                }
                
                // Create cancellation token source if not exists (never cancel it)
                if (_readCancellationTokenSource == null)
                {
                    _readCancellationTokenSource = new CancellationTokenSource();
                }
                
                var cancellationToken = _readCancellationTokenSource.Token;
                
                // Start independent reading task - runs on separate thread, NEVER stops
                _readTask = Task.Factory.StartNew(async () =>
                {
                    System.Diagnostics.Debug.WriteLine($"[RS485] Reading task started on thread {Thread.CurrentThread.ManagedThreadId} - NEVER STOPS");
                    
                    // This loop NEVER stops - it always tries to read (based on TaroziKlass)
                    while (true)
                    {
                        try
                        {
                            int currentFd = -1;
                            
                            // Check if file descriptor is valid
                            lock (_lockObject)
                            {
                                currentFd = _fd;
                            }
                            
                            if (currentFd >= 0)
                            {
                                try
                                {
                                    // Read data from serial port (12 bytes like in TaroziKlass)
                                    byte[]? data = AndroidSerialPort.Read(currentFd);
                                    
                                    if (data != null && data.Length > 0)
                                    {
                                        _lastDataTime = DateTime.Now;
                                        // Send raw data to main thread via invoke (non-blocking)
                                        byte[] capturedData = data; // Capture for closure
                                        MainThread.BeginInvokeOnMainThread(() =>
                                        {
                                            RawDataReceived?.Invoke(capturedData);
                                        });
                                    }
                                    else
                                    {
                                        if (CurrentWeight != 0 && (DateTime.Now - _lastDataTime).TotalSeconds >= 3)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[RS485] No data for 3s, clearing weight.");
                                            _lastDataTime = DateTime.Now; // Reset to avoid constant events
                                            MainThread.BeginInvokeOnMainThread(() =>
                                            {
                                                Clear();
                                            });
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[RS485] Read error (port may be closed): {ex.Message}");
                                    
                                    // Wait a bit before retrying (reduced delay)
                                    await Task.Delay(100).ConfigureAwait(false);
                                    continue;
                                }
                            }
                            else
                            {
                                // Port is not open, wait and retry (reduced delay)
                                System.Diagnostics.Debug.WriteLine("[RS485] Port not open, waiting for connection...");
                                await Task.Delay(200).ConfigureAwait(false);
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RS485] Unexpected error in reading loop: {ex.Message}");
                            
                            // Continue reading despite any error - task NEVER stops (reduced delay)
                            await Task.Delay(100).ConfigureAwait(false);
                            continue;
                        }
                        
                        // Reduced delay for better responsiveness
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            }
        }


        /// <summary>
        /// Stops reading from the serial port
        /// NOTE: This method does NOT stop the reading task - it only closes the port
        /// The reading task will continue running and wait for port to reopen
        /// </summary>
        public void Stop()
        {
            // Do NOT stop the reading task - it should NEVER stop
            // Just close the port, task will wait for it to reopen
            lock (_lockObject)
            {
                if (_fd >= 0)
                {
                    try
                    {
                        AndroidSerialPort.CloseFd(_fd);
                        System.Diagnostics.Debug.WriteLine("[RS485] Port closed, but reading task continues");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RS485] Error closing port: {ex.Message}");
                    }
                    _fd = -1;
                }
            }
        }

        /// <summary>
        /// Processes weight data from device type and updates UI
        /// </summary>
        public void ProcessWeightFromDevice(int weightGrams)
        {
            if (weightGrams != CurrentWeight)
            {
                CurrentWeight = weightGrams;
                WeightChanged?.Invoke(weightGrams);
            }
        }

        /// <summary>
        /// Clears the current weight value
        /// </summary>
        public void Clear()
        {
            CurrentWeight = 0;
            WeightChanged?.Invoke(0);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                lock (_lockObject)
                {
                    // Close port but DON'T stop reading task - it should NEVER stop
                    if (_fd >= 0)
                    {
                        try
                        {
                            AndroidSerialPort.CloseFd(_fd);
                        }
                        catch { }
                        _fd = -1;
                    }
                    
                    // Mark as disposed but reading task continues running
                    _disposed = true;
                    
                    System.Diagnostics.Debug.WriteLine("[RS485] Service disposed, but reading task continues running");
                }
            }
        }
    }
}

