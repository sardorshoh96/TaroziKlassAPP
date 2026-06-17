using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using System.IO;

namespace TaroziAPP.Services
{
    /// <summary>
    /// Service for printing to 701 thermal printer via serial port (9600 baud)
    /// </summary>
    public class Printer701Service : IPrinterService
    {
       
        private bool _disposed = false;
        private const string DefaultPort = "/dev/ttyS3"; // Default serial port for printer
        private const int DefaultBaudRate = 9600; // 9600 baud rate for 701
        private const string PrinterPortPreferenceKey = "printer_port";
        private const string PrinterBaudRatePreferenceKey = "printer_baud_rate";

        public string PortName { get; private set; }
        public int BaudRate { get; private set; }
        private int _fd = -1;
        private CancellationTokenSource? _readCancellationTokenSource;
        private Task? _readTask;


        public Printer701Service()
        {
            // Connect and start reading immediately
            Connect();
        }

        /// <summary>
        /// Connects to the printer and starts reading
        /// </summary>
        public int Connect()
        {
            _fd = AndroidSerialPort.Open(DefaultPort, DefaultBaudRate, 8, 1, AndroidSerialPort.Parity.None);
            if (_fd >= 0)
            {
                StartReading();
            }
            return _fd;
        }

        /// <summary>
        /// Starts reading from printer port in background
        /// </summary>
        private void StartReading()
        {
            _readCancellationTokenSource = new CancellationTokenSource();
            _readTask = Task.Run(async () =>
            {
                while (!_readCancellationTokenSource.Token.IsCancellationRequested && _fd >= 0)
                {
                    byte[]? data = AndroidSerialPort.Read(_fd);
                    // Data is read but not processed
                    await Task.Delay(100); // 100ms delay
                }
            }, _readCancellationTokenSource.Token);
        }

        /// <summary>
        /// Stops reading from printer port
        /// </summary>
        private void StopReading()
        {
            _readCancellationTokenSource?.Cancel();
            _readCancellationTokenSource?.Dispose();
            _readCancellationTokenSource = null;
            _readTask = null;
        }

        /// <summary>
        /// Prints receipt with weight, date, and address
        /// </summary>
        public async Task<bool> PrintReceiptAsync(double weightKg, DateTime dateTime, string address, string carNumber = "")
        {
            System.Diagnostics.Debug.WriteLine($"[Printer701] ▶ PrintReceiptAsync START: weight={weightKg}, fd={_fd}");
            // Use existing connection
            if (_fd < 0)
            {
                _fd = Connect();
                System.Diagnostics.Debug.WriteLine($"[Printer701] Reconnected fd={_fd}");
                if (_fd < 0)
                {
                    System.Diagnostics.Debug.WriteLine("[Printer701] ❌ Could not open port, aborting print");
                    return false;
                }
            }

            try
            {
                List<byte> buffer = new List<byte>();
                void AddBytes(byte[] data) => buffer.AddRange(data);
                void AddText(string text)
                {
                    string asciiText = text.Replace("'", "'").Replace("o'", "o").Replace("g'", "g");
                    buffer.AddRange(Encoding.UTF8.GetBytes(asciiText));
                }

                // Initialize printer (ESC @)
                AddBytes(new byte[] { 0x1B, 0x40 });
                
                // Set character code table to PC437
                AddBytes(new byte[] { 0x1B, 0x74, 0x00 }); 
                
                // Set character spacing to 0
                AddBytes(new byte[] { 0x1B, 0x20, 0x00 }); 
              
                // Set line spacing (ESC 3 24)
                AddBytes(new byte[] { 0x1B, 0x33, 0x18 }); 
              
                // Enable double strike ON
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 

                // Header - Center, Bold
                AddBytes(new byte[] { 0x1B, 0x61, 0x01 }); 
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); 
                AddBytes(new byte[] { 0x1B, 0x21, 0x00 }); 
                AddText("TAROZI KLASS\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); 
                AddBytes(new byte[] { 0x1B, 0x61, 0x00 }); 
                
                // Separator
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 
                AddText("  -----------------------------\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 

                // Car Number
                if (!string.IsNullOrWhiteSpace(carNumber))
                {
                    AddBytes(new byte[] { 0x1B, 0x61, 0x01 }); 
                    AddBytes(new byte[] { 0x1B, 0x21, 0x11 }); 
                    AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); 
                    AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 
                    AddText($"{carNumber}\n");
                    AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 
                    AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); 
                    AddBytes(new byte[] { 0x1B, 0x21, 0x00 }); 
                    AddBytes(new byte[] { 0x1B, 0x61, 0x00 }); 
                   
                    AddBytes(new byte[] { 0x1B, 0x47, 0x01 });
                    AddText("  -----------------------------\n");
                    AddBytes(new byte[] { 0x1B, 0x47, 0x00 });
                }

                // Date
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); 
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 
                AddText("   Sana: ");
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); 
                AddText(dateTime.ToString("dd.MM.yyyy"));
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 
                AddText("\n");

                // Time
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); 
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 
                AddText("   Vaqt: ");
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); 
                AddText(dateTime.ToString("HH:mm:ss"));
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 
                AddText("\n");

                // Separator
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 
                AddText("  -----------------------------\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 

                // Weight
                AddBytes(new byte[] { 0x1B, 0x61, 0x01 }); 
                AddBytes(new byte[] { 0x1B, 0x21, 0x11 }); 
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); 
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 
                AddText($"Og'irlik: {weightKg:0.###} kg\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); 
                AddBytes(new byte[] { 0x1B, 0x21, 0x00 }); 
                AddBytes(new byte[] { 0x1B, 0x61, 0x00 }); 
                
                // Separator
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 
                AddText("  -----------------------------\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 

                // Address
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); 
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 
                AddText("   Manzil: ");
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); 
                AddText(address);
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 
                AddText("\n");

                // Separator
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 
                AddText("  -----------------------------\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 

                // Website URL
                AddBytes(new byte[] { 0x1B, 0x61, 0x01 }); 
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); 
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); 
                AddText("https://taroziklass.uz/\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); 
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); 
                AddBytes(new byte[] { 0x1B, 0x61, 0x00 }); 

                // 3-4 lines feed
                

                // Partial cut (GS V 1)
                AddBytes(new byte[] { 0x1D, 0x56, 0x01 });
                
                await SendRawBytesAsync(buffer.ToArray(), _fd);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Printer701] Print error: {ex.Message}");
                return false;
            }
        }

        private async Task SendRawBytesAsync(byte[] command, int fd)
        {
            await Task.Run(() =>
            {
                AndroidSerialPort.Write(command, fd);
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopReading();
                if (_fd >= 0)
                {
                    AndroidSerialPort.CloseFd(_fd);
                    _fd = -1;
                }
                _disposed = true;
            }
        }
    }
}
