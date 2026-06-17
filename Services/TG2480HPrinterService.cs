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
    /// Service for printing to TG2480H thermal printer via serial port
    /// </summary>
    public class TG2480HPrinterService : IPrinterService
    {
       
        private bool _disposed = false;
        private const string DefaultPort = "/dev/ttyS3"; // Default serial port for printer
        private const int DefaultBaudRate = 115200; // TG2480H default baud rate
        private const string PrinterPortPreferenceKey = "printer_port";
        private const string PrinterBaudRatePreferenceKey = "printer_baud_rate";

        public string PortName { get; private set; }
        public int BaudRate { get; private set; }
        private int _fd = -1;
        private CancellationTokenSource? _readCancellationTokenSource;
        private Task? _readTask;


        public TG2480HPrinterService()
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
            // Use existing connection
            if (_fd < 0)
            {
                _fd = Connect();
                if (_fd < 0)
                {
                    return false;
                }
            }

            try
            {
                // Initialize printer (ESC @) - Reset printer
                await SendRawBytesAsync(new byte[] { 0x1B, 0x40 }, _fd);
                await Task.Delay(100);

                // Set character code table to PC437 (ESC t 0)
                // PC437 = 0, PC850 = 1, PC860 = 2, PC863 = 3, PC865 = 4, PC858 = 5, PC866 = 6, VISCII = 7, U.D.P. = 8
                await SendRawBytesAsync(new byte[] { 0x1B, 0x74, 0x00 }, _fd); // PC437
                await Task.Delay(50);

                // Set character spacing to 0 for tighter text
                // ESC SP n - Set character spacing (n=0-255, 0=default)
                await SendRawBytesAsync(new byte[] { 0x1B, 0x20, 0x00 }, _fd); // No extra spacing
                await Task.Delay(50);

                // Set line spacing to default for better clarity
                // ESC 3 n - Set line spacing (n=0-255, default=30)
                await SendRawBytesAsync(new byte[] { 0x1B, 0x33, 0x18 }, _fd); // 24 dots line spacing
                await Task.Delay(50);

                // Enable double strike globally for all text (except where explicitly disabled)
                // This makes all text darker and clearer
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // ESC G 1 = Double strike ON
                await Task.Delay(50);

                // Print header - Center aligned, Bold, Normal font (11 CPI) with enhanced clarity
                await SendRawBytesAsync(new byte[] { 0x1B, 0x61, 0x01 }, _fd); // ESC a 1 = Center
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x01 }, _fd); // ESC E 1 = Bold ON
                await SendRawBytesAsync(new byte[] { 0x1B, 0x21, 0x00 }, _fd); // ESC ! 0 = Normal size (11 CPI)
                await SendRawTextAsync("TAROZI KLASS\n", _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x00 }, _fd); // ESC E 0 = Bold OFF
                await SendRawBytesAsync(new byte[] { 0x1B, 0x61, 0x00 }, _fd); // ESC a 0 = Left align
                await Task.Delay(50);

                // Print separator line - with enhanced clarity
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // ESC G 1 = Double strike ON
                await SendRawTextAsync("  --------------------------------\n", _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF

                // Print car number (if provided) - Center aligned, Large font
                if (!string.IsNullOrWhiteSpace(carNumber))
                {
                    await SendRawBytesAsync(new byte[] { 0x1B, 0x61, 0x01 }, _fd); // Center align
                    await SendRawBytesAsync(new byte[] { 0x1B, 0x21, 0x11 }, _fd); // Double width + height
                    await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x01 }, _fd); // Bold ON
                    await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // Double strike ON
                    await SendRawTextAsync($"{carNumber}\n", _fd);
                    await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // Double strike OFF
                    await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x00 }, _fd); // Bold OFF
                    await SendRawBytesAsync(new byte[] { 0x1B, 0x21, 0x00 }, _fd); // Normal size
                    await SendRawBytesAsync(new byte[] { 0x1B, 0x61, 0x00 }, _fd); // Left align
                    await Task.Delay(50);

                    // Separator after car number
                    await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd);
                    await SendRawTextAsync("  --------------------------------\n", _fd);
                    await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd);
                }

                // Print date - with enhanced clarity (bold label, normal value with double strike)
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x01 }, _fd); // ESC E 1 = Bold ON
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // ESC G 1 = Double strike ON
                await SendRawTextAsync("   Sana: ", _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x00 }, _fd); // ESC E 0 = Bold OFF
                await SendRawTextAsync(dateTime.ToString("dd.MM.yyyy"), _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF
                await SendRawTextAsync("\n", _fd);

                // Print time - with enhanced clarity (bold label, normal value with double strike)
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x01 }, _fd); // ESC E 1 = Bold ON
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // ESC G 1 = Double strike ON
                await SendRawTextAsync("   Vaqt: ", _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x00 }, _fd); // ESC E 0 = Bold OFF
                await SendRawTextAsync(dateTime.ToString("HH:mm:ss"), _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF
                await SendRawTextAsync("\n", _fd);

                // Print separator - with enhanced clarity
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // ESC G 1 = Double strike ON
                await SendRawTextAsync("  --------------------------------\n", _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF

                // Print weight - Center aligned, Large font (Double width + height) with enhanced clarity
                await SendRawBytesAsync(new byte[] { 0x1B, 0x61, 0x01 }, _fd); // Center align
                await SendRawBytesAsync(new byte[] { 0x1B, 0x21, 0x11 }, _fd); // ESC ! 0x11 = Double width + height (11 CPI)
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x01 }, _fd); // ESC E 1 = Bold ON
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // ESC G 1 = Double strike ON for darker text
                await SendRawTextAsync($"Og'irlik: {weightKg:0.###} kg\n", _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x00 }, _fd); // ESC E 0 = Bold OFF
                await SendRawBytesAsync(new byte[] { 0x1B, 0x21, 0x00 }, _fd); // ESC ! 0x00 = Normal size
                await SendRawBytesAsync(new byte[] { 0x1B, 0x61, 0x00 }, _fd); // Left align
                await Task.Delay(50);

                // Print separator - with enhanced clarity
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // ESC G 1 = Double strike ON
                await SendRawTextAsync("  --------------------------------\n", _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF

                // Print address - with enhanced clarity (bold label, normal value with double strike)
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x01 }, _fd); // ESC E 1 = Bold ON
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // ESC G 1 = Double strike ON
                await SendRawTextAsync("   Manzil: ", _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x00 }, _fd); // ESC E 0 = Bold OFF
                await SendRawTextAsync(address, _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF
                await SendRawTextAsync("\n", _fd);
                // Print separator - with enhanced clarity
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // ESC G 1 = Double strike ON
                await SendRawTextAsync("  --------------------------------\n", _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF

                // Print website URL - Center aligned
                await SendRawBytesAsync(new byte[] { 0x1B, 0x61, 0x01 }, _fd); // ESC a 1 = Center align
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x01 }, _fd); // ESC E 1 = Bold ON
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x01 }, _fd); // ESC G 1 = Double strike ON
                await SendRawTextAsync("https://taroziklass.uz/\n", _fd);
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF
                await SendRawBytesAsync(new byte[] { 0x1B, 0x45, 0x00 }, _fd); // ESC E 0 = Bold OFF
                await SendRawBytesAsync(new byte[] { 0x1B, 0x61, 0x00 }, _fd); // ESC a 0 = Left align
                await Task.Delay(50);

                // Disable double strike before feed and cut
                await SendRawBytesAsync(new byte[] { 0x1B, 0x47, 0x00 }, _fd); // ESC G 0 = Double strike OFF
                await Task.Delay(50);

                // Feed paper (3 lines) - Use ESC d instead of multiple LF
                await SendRawBytesAsync(new byte[] { 0x1B, 0x64, 0x03 }, _fd); // ESC d 3 = Feed 3 lines
                await Task.Delay(50);

                // Cut paper - Try multiple cut commands
                // GS V 0 = Partial cut
                await SendRawBytesAsync(new byte[] { 0x1D, 0x56, 0x00 }, _fd);
                await Task.Delay(50);
                
                // Alternative: ESC i (Partial cut)
                await SendRawBytesAsync(new byte[] { 0x1B, 0x69 }, _fd);
                await Task.Delay(100);
                
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

