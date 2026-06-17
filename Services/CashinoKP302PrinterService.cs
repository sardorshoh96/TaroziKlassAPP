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
    /// Service for printing to Cashino KP-302 thermal printer via serial port
    /// </summary>
    public class CashinoKP302PrinterService : IPrinterService
    {
       
        private bool _disposed = false;
        private const string DefaultPort = "/dev/ttyS3"; // Default serial port for printer
        private const int DefaultBaudRate = 115200; // default baud rate
        private const string PrinterPortPreferenceKey = "printer_port";
        private const string PrinterBaudRatePreferenceKey = "printer_baud_rate";

        public string PortName { get; private set; }
        public int BaudRate { get; private set; }
        private int _fd = -1;
        private CancellationTokenSource? _readCancellationTokenSource;
        private Task? _readTask;


        public CashinoKP302PrinterService()
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
                    // Data is read but not processed (printer response handling if needed)
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
            // Don't block - let task complete asynchronously
            // Task will complete when cancellation token is triggered
            _readCancellationTokenSource?.Dispose();
            _readCancellationTokenSource = null;
            _readTask = null;
        }

        /// <summary>
        /// Test print - sends diagnostic command
        /// </summary>
        

        /// <summary>
        /// Prints receipt with weight, date, and address
        /// </summary>
        /// <param name="weightKg">Weight in kilograms</param>
        /// <param name="dateTime">Date and time</param>
        /// <param name="address">Address</param>
        public async Task<bool> PrintReceiptAsync(double weightKg, DateTime dateTime, string address, string carNumber = "")
        {
            System.Diagnostics.Debug.WriteLine($"[CashinoPrinter] ▶ PrintReceiptAsync START: weight={weightKg}, fd={_fd}");
            // Use existing connection
            if (_fd < 0)
            {
                _fd = Connect();
                System.Diagnostics.Debug.WriteLine($"[CashinoPrinter] Reconnected fd={_fd}");
                if (_fd < 0)
                {
                    System.Diagnostics.Debug.WriteLine("[CashinoPrinter] ❌ Could not open port, aborting print");
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
                // Initialize printer (ESC @) - Reset printer
                AddBytes(new byte[] { 0x1B, 0x40 });
                
                // Set character code table to PC437 (ESC t 0)
                // PC437 = 0, PC850 = 1, PC860 = 2, PC863 = 3, PC865 = 4, PC858 = 5, PC866 = 6, VISCII = 7, U.D.P. = 8
                AddBytes(new byte[] { 0x1B, 0x74, 0x00 }); // PC437
                

                // Set character spacing to 0 for tighter text
                // ESC SP n - Set character spacing (n=0-255, 0=default)
                AddBytes(new byte[] { 0x1B, 0x20, 0x00 }); // No extra spacing
              
                // Set line spacing to default for better clarity
                // ESC 3 n - Set line spacing (n=0-255, default=30)
                AddBytes(new byte[] { 0x1B, 0x33, 0x18 }); // 24 dots line spacing
              

                // Enable double strike globally for all text (except where explicitly disabled)
                // This makes all text darker and clearer
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // ESC G 1 = Double strike ON
               

                // Print header - Center aligned, Bold, Normal font (11 CPI) with enhanced clarity
                AddBytes(new byte[] { 0x1B, 0x61, 0x01 }); // ESC a 1 = Center
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); // ESC E 1 = Bold ON
                AddBytes(new byte[] { 0x1B, 0x21, 0x00 }); // ESC ! 0 = Normal size (11 CPI)
                AddText("TAROZI KLASS\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // ESC G 0 = Double strike OFF
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); // ESC E 0 = Bold OFF
                AddBytes(new byte[] { 0x1B, 0x61, 0x00 }); // ESC a 0 = Left align
                

                // Print separator line - with enhanced clarity
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // ESC G 1 = Double strike ON
                AddText("  --------------------------------\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // ESC G 0 = Double strike OFF

                // Print car number (if provided) - Center aligned, Large font
                if (!string.IsNullOrWhiteSpace(carNumber))
                {
                    AddBytes(new byte[] { 0x1B, 0x61, 0x01 }); // Center align
                    AddBytes(new byte[] { 0x1B, 0x21, 0x11 }); // Double width + height
                    AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); // Bold ON
                    AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // Double strike ON
                    AddText($"{carNumber}\n");
                    AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // Double strike OFF
                    AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); // Bold OFF
                    AddBytes(new byte[] { 0x1B, 0x21, 0x00 }); // Normal size
                    AddBytes(new byte[] { 0x1B, 0x61, 0x00 }); // Left align
                   

                    // Separator after car number
                    AddBytes(new byte[] { 0x1B, 0x47, 0x01 });
                    AddText("  --------------------------------\n");
                    AddBytes(new byte[] { 0x1B, 0x47, 0x00 });
                }

                // Print date - with enhanced clarity (bold label, normal value with double strike)
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); // ESC E 1 = Bold ON
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // ESC G 1 = Double strike ON
                AddText("   Sana: ");
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); // ESC E 0 = Bold OFF
                AddText(dateTime.ToString("dd.MM.yyyy"));
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // ESC G 0 = Double strike OFF
                AddText("\n");

                // Print time - with enhanced clarity (bold label, normal value with double strike)
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); // ESC E 1 = Bold ON
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // ESC G 1 = Double strike ON
                AddText("   Vaqt: ");
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); // ESC E 0 = Bold OFF
                AddText(dateTime.ToString("HH:mm:ss"));
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // ESC G 0 = Double strike OFF
                AddText("\n");

                // Print separator - with enhanced clarity
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // ESC G 1 = Double strike ON
                AddText("  --------------------------------\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // ESC G 0 = Double strike OFF

                // Print weight - Center aligned, Large font (Double width + height) with enhanced clarity
                AddBytes(new byte[] { 0x1B, 0x61, 0x01 }); // Center align
                AddBytes(new byte[] { 0x1B, 0x21, 0x11 }); // ESC ! 0x11 = Double width + height (11 CPI)
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); // ESC E 1 = Bold ON
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // ESC G 1 = Double strike ON for darker text
                AddText($"Og'irlik: {weightKg:0.###} kg\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // ESC G 0 = Double strike OFF
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); // ESC E 0 = Bold OFF
                AddBytes(new byte[] { 0x1B, 0x21, 0x00 }); // ESC ! 0x00 = Normal size
                AddBytes(new byte[] { 0x1B, 0x61, 0x00 }); // Left align
                

                // Print separator - with enhanced clarity
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // ESC G 1 = Double strike ON
                AddText("  --------------------------------\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // ESC G 0 = Double strike OFF

                // Print address - with enhanced clarity (bold label, normal value with double strike)
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); // ESC E 1 = Bold ON
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // ESC G 1 = Double strike ON
                AddText("   Manzil: ");
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); // ESC E 0 = Bold OFF
                AddText(address);
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // ESC G 0 = Double strike OFF
                AddText("\n");
                // Print separator - with enhanced clarity
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // ESC G 1 = Double strike ON
                AddText("  --------------------------------\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // ESC G 0 = Double strike OFF

                // Print website URL - Center aligned
                AddBytes(new byte[] { 0x1B, 0x61, 0x01 }); // ESC a 1 = Center align
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 }); // ESC E 1 = Bold ON
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 }); // ESC G 1 = Double strike ON
                AddText("https://taroziklass.uz/\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 }); // ESC G 0 = Double strike OFF
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 }); // ESC E 0 = Bold OFF
                AddBytes(new byte[] { 0x1B, 0x61, 0x00 }); // ESC a 0 = Left align

                // 3-4 qator bo'sh joy tashlash
                AddText("\n\n\n\n");

                // Qog'ozni kesish (Partial cut)
                AddBytes(new byte[] { 0x1D, 0x56, 0x01 });
                
                
                await SendRawBytesAsync(buffer.ToArray(), _fd);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sends text to printer with enhanced clarity
        /// </summary>
        private async Task SendRawTextAsync(string text, int fd)
        {
            // Convert to ASCII/CP437 compatible encoding
            // Replace Cyrillic/Uzbek characters with ASCII equivalents
            string asciiText = text
                .Replace("'", "'")
                .Replace("'", "'")
                .Replace("o'", "o")
                .Replace("g'", "g")
                .Replace("'", "'");
            
            // Use UTF-8 encoding instead of ASCII for better compatibility
            byte[] data = Encoding.UTF8.GetBytes(asciiText);
            
            await Task.Run(() =>
            {
                AndroidSerialPort.Write(data, fd);
            });
            
            // Small delay after each text to ensure printer processes it
            await Task.Delay(10);
        }

        /// <summary>
        /// Sends raw bytes to printer
        /// </summary>
        private async Task SendRawBytesAsync(byte[] command, int fd)
        {
            await Task.Run(() =>
            {
                AndroidSerialPort.Write(command, fd);
            });
        }

        /// <summary>
        /// Prints 2D barcode (QR code) using ESC/POS commands
        /// GS k 4 L pL pH cn fn n1 n2 [data]
        /// </summary>
        private async Task PrintQRCodeAsync(string data, int fd)
        {
            // Set center alignment before printing QR code
            await SendRawBytesAsync(new byte[] { 0x1B, 0x61, 0x01 }, _fd); // ESC a 1 = Center align
            await Task.Delay(100);
            
            byte[] qrData = Encoding.UTF8.GetBytes(data);
            int dataLength = qrData.Length;
            
            // Calculate pL and pH (low and high bytes of data length + 4)
            // pL and pH represent the length of data + 4 (for L, cn, fn, n1, n2)
            int totalLength = dataLength + 4;
            int pL = totalLength & 0xFF;
            int pH = (totalLength >> 8) & 0xFF;
            
            // Step 1: Store QR code data using GS k 4 format
            // GS k 4 L pL pH cn fn n1 n2 [data]
            // L = 0 (fixed)
            // cn = 49 (0x31) = QR code model 2
            // fn = 65 (0x41) = Function A - Store data
            // n1 = 0, n2 = 0 = Error correction level M
            List<byte> storeCommand = new List<byte>();
            storeCommand.Add(0x1D); // GS
            storeCommand.Add(0x6B); // k
            storeCommand.Add(0x04); // 4 (QR code)
            storeCommand.Add(0x00); // L (fixed)
            storeCommand.Add((byte)pL); // pL (low byte)
            storeCommand.Add((byte)pH); // pH (high byte)
            storeCommand.Add(0x31); // cn = 49 (QR code model 2)
            storeCommand.Add(0x41); // fn = 65 (Function A - Store)
            storeCommand.Add(0x00); // n1 = 0 (Error correction level M)
            storeCommand.Add(0x00); // n2 = 0
            storeCommand.AddRange(qrData); // QR code data
            
            await SendRawBytesAsync(storeCommand.ToArray(), fd);
            await Task.Delay(300);
            
            // Step 2: Print QR code using GS k 4 format
            // GS k 4 L 0 0 cn fn n1 n2
            // cn = 49, fn = 81 (0x51) = Function C - Print
            List<byte> printCommand = new List<byte>();
            printCommand.Add(0x1D); // GS
            printCommand.Add(0x6B); // k
            printCommand.Add(0x04); // 4
            printCommand.Add(0x00); // L
            printCommand.Add(0x00); // pL = 0 (no data in print command)
            printCommand.Add(0x00); // pH = 0
            printCommand.Add(0x31); // cn = 49
            printCommand.Add(0x51); // fn = 81 (Function C - Print)
            printCommand.Add(0x00); // n1 = 0
            printCommand.Add(0x00); // n2 = 0
            
            await SendRawBytesAsync(printCommand.ToArray(), fd);
            await Task.Delay(300);
            
            // Reset to left align after QR code
            await SendRawBytesAsync(new byte[] { 0x1B, 0x61, 0x00 }, _fd); // ESC a 0 = Left align
            await Task.Delay(100);
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


