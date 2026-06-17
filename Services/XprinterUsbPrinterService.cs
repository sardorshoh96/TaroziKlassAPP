using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

#if ANDROID
using TaroziAPP.Platforms.Android;
#endif

namespace TaroziAPP.Services
{
    /// <summary>
    /// Xprinter USB printer service for rooted Android (RK3568).
    /// Auto-detects /dev/usb/lp* device, grants permissions via root (su),
    /// then writes raw ESC/POS commands directly to the device file.
    /// </summary>
    public class XprinterUsbPrinterService : IPrinterService
    {
        private const string UsbDevDir = "/dev/usb";
        private const string LpPrefix = "lp";
        private const string BusUsbDir = "/dev/bus/usb";

        // Known printer: Printer-80 (vendor=1fc9, product=2016)
        // Device node: /dev/bus/usb/001/004
        private const string PrinterBusPath = "/dev/bus/usb/001/004";

        private bool _disposed = false;
        private string? _detectedPath = null;

#if ANDROID
        // Android USB Host API driver (preferred — no root needed)
        private AndroidUsbPrinterService? _androidUsbDriver;
#endif

        public XprinterUsbPrinterService()
        {
#if ANDROID
            // Android USB Host API orqali ulanish (root talab qilmaydi)
            _androidUsbDriver = new AndroidUsbPrinterService();
            bool connected = _androidUsbDriver.Connect();
            if (connected)
            {
                Debug.WriteLine("[XprinterUSB] ✅ Android USB Host API orqali ulandi");
                return; // Root usulga o'tmaslik
            }
            Debug.WriteLine("[XprinterUSB] ⚠️ Android USB API ishlamadi, root fallback urinilmoqda...");
#endif
            // Fallback: root orqali /dev/bus/usb yoki /dev/usb/lp*
            TryLoadUsblpModule();
            _detectedPath = DetectAndGrantPermission();
            Debug.WriteLine($"[XprinterUSB] Initialized. Detected path: {_detectedPath ?? "none"}");
        }

        /// <summary>
        /// No-op for interface compatibility. Returns 0 if printer found, -1 otherwise.
        /// </summary>
        public int Connect()
        {
#if ANDROID
            if (_androidUsbDriver != null)
            {
                bool ok = _androidUsbDriver.Connect();
                if (ok) return 0;
            }
#endif
            _detectedPath = DetectAndGrantPermission();
            return _detectedPath != null ? 0 : -1;
        }

        // ─────────────────────────────────────────────────────────────────────
        // USB device detection + root permission grant
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans /dev/usb/ for lp* files, grants 666 permission via su, returns path.
        /// </summary>
        private string? DetectAndGrantPermission()
        {
            try
            {
                // 1. Scan /dev/usb/ for lp0, lp1, lp2 ...
                var candidate = FindUsbPrinterPath();

                if (candidate == null)
                {
                    Debug.WriteLine("[XprinterUSB] ⚠️  No /dev/usb/lp* device found. Is printer plugged in?");
                    return null;
                }

                // 2. Grant rw permission for all users via root
                var chmodOk = RunAsRoot($"chmod 666 {candidate}");
                if (chmodOk)
                    Debug.WriteLine($"[XprinterUSB] ✅ chmod 666 {candidate} succeeded");
                else
                    Debug.WriteLine($"[XprinterUSB] ⚠️  chmod failed for {candidate}, will try anyway");

                return candidate;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[XprinterUSB] DetectAndGrantPermission error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Searches /dev/usb/ directory for the first available lp* device file.
        /// Falls back to root (su) scan, then /dev/bus/usb/ if normal access fails.
        /// </summary>
        private static string? FindUsbPrinterPath()
        {
            // 1. Direct check for the most common paths (lp0..lp3)
            for (int i = 0; i <= 3; i++)
            {
                var path = $"{UsbDevDir}/{LpPrefix}{i}";
                if (File.Exists(path))
                {
                    Debug.WriteLine($"[XprinterUSB] Found device: {path}");
                    return path;
                }
            }

            // 2. Normal directory scan
            try
            {
                if (Directory.Exists(UsbDevDir))
                {
                    foreach (var file in Directory.GetFiles(UsbDevDir, $"{LpPrefix}*"))
                    {
                        Debug.WriteLine($"[XprinterUSB] Found via scan: {file}");
                        return file;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[XprinterUSB] Directory scan error: {ex.Message}");
            }

            // 3. Root fallback: use 'su -c ls /dev/usb/'
            Debug.WriteLine("[XprinterUSB] Trying root fallback: su -c ls /dev/usb/");
            var rootFound = FindUsbPrinterPathViaRoot();
            if (rootFound != null)
                return rootFound;

            // 4. Alt paths (/dev/lp*, /dev/usblp*)
            string[] altPaths = { "/dev/lp0", "/dev/lp1", "/dev/usblp0", "/dev/usblp1" };
            foreach (var alt in altPaths)
            {
                if (File.Exists(alt))
                {
                    Debug.WriteLine($"[XprinterUSB] Found alt device: {alt}");
                    return alt;
                }
            }

            // 5. Known bus path fallback: /dev/bus/usb/001/004 (Printer-80, vendor=1fc9)
            //    usblp kernel module not loaded — use raw USB node directly
            Debug.WriteLine("[XprinterUSB] Trying known bus path: " + PrinterBusPath);
            var busPath = FindPrinterInBusUsb();
            if (busPath != null)
                return busPath;

            return null;
        }

        /// <summary>
        /// Scans /dev/bus/usb/ for the Xprinter device node by checking sysfs idVendor.
        /// Printer-80 vendor=1fc9.
        /// </summary>
        private static string? FindPrinterInBusUsb()
        {
            try
            {
                // Read sysfs to find busnum/devnum for vendor 1fc9
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "su",
                    Arguments = "-c \"for d in /sys/bus/usb/devices/[0-9]*; do v=$(cat $d/idVendor 2>/dev/null); if [ '$v' = '1fc9' ]; then b=$(cat $d/busnum); n=$(cat $d/devnum); printf '%03d/%03d\\n' $b $n; fi; done\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                process.Start();
                process.WaitForExit(3000);
                var output = process.StandardOutput.ReadToEnd().Trim();
                Debug.WriteLine($"[XprinterUSB] FindPrinterInBusUsb output: '{output}'");

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = $"{BusUsbDir}/{line.Trim()}";
                    Debug.WriteLine($"[XprinterUSB] Bus path candidate: {candidate}");
                    return candidate; // Return first match
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[XprinterUSB] FindPrinterInBusUsb error: {ex.Message}");
            }

            // Hardcoded fallback (device node confirmed by ADB scan)
            if (File.Exists(PrinterBusPath))
            {
                Debug.WriteLine($"[XprinterUSB] Using hardcoded bus path: {PrinterBusPath}");
                return PrinterBusPath;
            }

            return null;
        }

        /// <summary>
        /// Tries to load the usblp kernel module via insmod/modprobe.
        /// If successful, /dev/usb/lp0 will appear automatically.
        /// </summary>
        private static void TryLoadUsblpModule()
        {
            try
            {
                // Try modprobe first
                RunAsRoot("modprobe usblp 2>/dev/null");
                // Also try insmod from common paths
                RunAsRoot("insmod /system/lib/modules/usblp.ko 2>/dev/null || insmod /vendor/lib/modules/usblp.ko 2>/dev/null");
                System.Threading.Thread.Sleep(500); // Wait for udev to create node
                Debug.WriteLine("[XprinterUSB] usblp module load attempted");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[XprinterUSB] TryLoadUsblpModule error: {ex.Message}");
            }
        }

        /// <summary>
        /// Uses 'su -c ls /dev/usb/' to find lp* devices when normal access is denied.
        /// </summary>
        private static string? FindUsbPrinterPathViaRoot()
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "su",
                    Arguments = "-c \"ls /dev/usb/ 2>/dev/null && ls /dev/ | grep lp 2>/dev/null\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                process.Start();
                bool finished = process.WaitForExit(3000);
                if (!finished) { process.Kill(); return null; }

                var output = process.StandardOutput.ReadToEnd().Trim();
                Debug.WriteLine($"[XprinterUSB] su ls output: '{output}'");

                // Parse output lines looking for lp* entries
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("lp", StringComparison.OrdinalIgnoreCase))
                    {
                        // Could be "lp0" from /dev/usb/ or just "lp0" from /dev/
                        var candidate = $"{UsbDevDir}/{trimmed}";
                        Debug.WriteLine($"[XprinterUSB] Root found candidate: {candidate}");
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[XprinterUSB] FindUsbPrinterPathViaRoot error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Runs a shell command as root using 'su -c'.
        /// Returns true if the process exited with code 0.
        /// </summary>
        private static bool RunAsRoot(string command)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "su",
                    Arguments = $"-c \"{command}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                process.Start();
                bool finished = process.WaitForExit(3000); // 3s timeout
                if (!finished)
                {
                    process.Kill();
                    Debug.WriteLine($"[XprinterUSB] su command timed out: {command}");
                    return false;
                }

                var exitCode = process.ExitCode;
                Debug.WriteLine($"[XprinterUSB] su '{command}' exit={exitCode}");
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[XprinterUSB] RunAsRoot error: {ex.Message}");
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Print logic
        // ─────────────────────────────────────────────────────────────────────

        public async Task<bool> PrintReceiptAsync(
            double weightKg, DateTime dateTime, string address, string carNumber = "")
        {
            Debug.WriteLine($"[XprinterUSB] ▶ PrintReceiptAsync: weight={weightKg}");

#if ANDROID
            // 1. Android USB Host API orqali urinish (root talab qilmaydi)
            if (_androidUsbDriver != null)
            {
                bool connected = _androidUsbDriver.Connect();
                if (connected)
                {
                    var buffer = BuildEscPosBuffer(weightKg, dateTime, address, carNumber);
                    bool sent = _androidUsbDriver.Write(buffer);
                    if (sent)
                    {
                        Debug.WriteLine($"[XprinterUSB] ✅ Android USB orqali chek yuborildi ({buffer.Length} bytes)");
                        return true;
                    }
                    Debug.WriteLine("[XprinterUSB] ⚠️ Android USB write failed, root fallback urinilmoqda...");
                }
                else
                {
                    Debug.WriteLine("[XprinterUSB] ⚠️ Android USB connect failed, root fallback urinilmoqda...");
                }
            }
#endif

            // 2. Fallback: root orqali /dev/usb/lp* yoki /dev/bus/usb
            _detectedPath = DetectAndGrantPermission();

            if (_detectedPath == null)
            {
                Debug.WriteLine("[XprinterUSB] ❌ Printer not found. Aborting.");
                return false;
            }

            try
            {
                var data = BuildEscPosBuffer(weightKg, dateTime, address, carNumber);
                await WriteToDeviceAsync(data);

                Debug.WriteLine($"[XprinterUSB] ✅ Print done. Bytes: {data.Length}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[XprinterUSB] ❌ Print error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ESC/POS chek ma'lumotlarini byte array sifatida yaratadi.
        /// Android USB driver va root fallback ikkalasi ham shu metoddan foydalanadi.
        /// </summary>
        internal static byte[] BuildEscPosBuffer(
            double weightKg, DateTime dateTime, string address, string carNumber = "")
        {
            var buffer = new List<byte>();

            void AddBytes(byte[] data) => buffer.AddRange(data);
            void AddText(string text)
            {
                var ascii = text
                    .Replace("\u2018", "'").Replace("\u2019", "'")
                    .Replace("o\u2019", "o").Replace("g\u2019", "g")
                    .Replace("O\u2019", "O").Replace("G\u2019", "G")
                    .Replace("\u02bc", "'");
                buffer.AddRange(Encoding.UTF8.GetBytes(ascii));
            }

            // ── Reset printer ──────────────────────────────────────────
            AddBytes(new byte[] { 0x1B, 0x40 });           // ESC @
            AddBytes(new byte[] { 0x1B, 0x74, 0x00 });     // char table PC437
            AddBytes(new byte[] { 0x1B, 0x20, 0x00 });     // no extra spacing
            AddBytes(new byte[] { 0x1B, 0x33, 0x18 });     // line spacing 24 dots
            AddBytes(new byte[] { 0x1B, 0x47, 0x01 });     // double strike ON

            // ── Header ────────────────────────────────────────────────
            AddBytes(new byte[] { 0x1B, 0x61, 0x01 });     // center
            AddBytes(new byte[] { 0x1B, 0x45, 0x01 });     // bold ON
            AddBytes(new byte[] { 0x1B, 0x21, 0x00 });     // normal size
            AddText("TAROZI KLASS\n");
            AddBytes(new byte[] { 0x1B, 0x47, 0x00 });     // double strike OFF
            AddBytes(new byte[] { 0x1B, 0x45, 0x00 });     // bold OFF
            AddBytes(new byte[] { 0x1B, 0x61, 0x00 });     // left

            // ── Separator ─────────────────────────────────────────────
            AddBytes(new byte[] { 0x1B, 0x47, 0x01 });
            AddText("  --------------------------------\n");
            AddBytes(new byte[] { 0x1B, 0x47, 0x00 });

            // ── Car number ────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(carNumber))
            {
                AddBytes(new byte[] { 0x1B, 0x61, 0x01 });   // center
                AddBytes(new byte[] { 0x1B, 0x21, 0x11 });   // double width+height
                AddBytes(new byte[] { 0x1B, 0x45, 0x01 });   // bold ON
                AddBytes(new byte[] { 0x1B, 0x47, 0x01 });   // double strike ON
                AddText($"{carNumber}\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 });
                AddBytes(new byte[] { 0x1B, 0x45, 0x00 });
                AddBytes(new byte[] { 0x1B, 0x21, 0x00 });   // normal size
                AddBytes(new byte[] { 0x1B, 0x61, 0x00 });   // left

                AddBytes(new byte[] { 0x1B, 0x47, 0x01 });
                AddText("  --------------------------------\n");
                AddBytes(new byte[] { 0x1B, 0x47, 0x00 });
            }

            // ── Date ──────────────────────────────────────────────────
            AddBytes(new byte[] { 0x1B, 0x45, 0x01, 0x1B, 0x47, 0x01 });
            AddText("   Sana: ");
            AddBytes(new byte[] { 0x1B, 0x45, 0x00 });
            AddText(dateTime.ToString("dd.MM.yyyy"));
            AddBytes(new byte[] { 0x1B, 0x47, 0x00 });
            AddText("\n");

            // ── Time ──────────────────────────────────────────────────
            AddBytes(new byte[] { 0x1B, 0x45, 0x01, 0x1B, 0x47, 0x01 });
            AddText("   Vaqt: ");
            AddBytes(new byte[] { 0x1B, 0x45, 0x00 });
            AddText(dateTime.ToString("HH:mm:ss"));
            AddBytes(new byte[] { 0x1B, 0x47, 0x00 });
            AddText("\n");

            // ── Separator ─────────────────────────────────────────────
            AddBytes(new byte[] { 0x1B, 0x47, 0x01 });
            AddText("  --------------------------------\n");
            AddBytes(new byte[] { 0x1B, 0x47, 0x00 });

            // ── Weight (2x katta, markazda) ───────────────────────────
            AddBytes(new byte[] { 0x1B, 0x61, 0x01 });     // center
            AddBytes(new byte[] { 0x1D, 0x21, 0x11 });     // 2x width × 2x height (GS !)
            AddBytes(new byte[] { 0x1B, 0x45, 0x01 });     // bold ON
            AddBytes(new byte[] { 0x1B, 0x47, 0x01 });     // double strike ON
            AddText($"Og'irlik: {weightKg:0.###} kg\n");
            AddBytes(new byte[] { 0x1B, 0x47, 0x00 });
            AddBytes(new byte[] { 0x1B, 0x45, 0x00 });
            AddBytes(new byte[] { 0x1D, 0x21, 0x00 });     // normal size (GS ! reset)
            AddBytes(new byte[] { 0x1B, 0x61, 0x00 });     // left

            // ── Separator ─────────────────────────────────────────────
            AddBytes(new byte[] { 0x1B, 0x47, 0x01 });
            AddText("  --------------------------------\n");
            AddBytes(new byte[] { 0x1B, 0x47, 0x00 });

            // ── Address ───────────────────────────────────────────────
            AddBytes(new byte[] { 0x1B, 0x45, 0x01, 0x1B, 0x47, 0x01 });
            AddText("   Manzil: ");
            AddBytes(new byte[] { 0x1B, 0x45, 0x00 });
            AddText(address);
            AddBytes(new byte[] { 0x1B, 0x47, 0x00 });
            AddText("\n");

            // ── Separator ─────────────────────────────────────────────
            AddBytes(new byte[] { 0x1B, 0x47, 0x01 });
            AddText("  --------------------------------\n");
            AddBytes(new byte[] { 0x1B, 0x47, 0x00 });

            // ── URL ───────────────────────────────────────────────────
            AddBytes(new byte[] { 0x1B, 0x61, 0x01 });     // center
            AddBytes(new byte[] { 0x1B, 0x45, 0x01, 0x1B, 0x47, 0x01 });
            AddText("https://taroziklass.uz/\n");
            AddBytes(new byte[] { 0x1B, 0x47, 0x00, 0x1B, 0x45, 0x00 });
            AddBytes(new byte[] { 0x1B, 0x61, 0x00 });     // left

            // ── Feed + cut ────────────────────────────────────────────
            AddText("\n\n\n\n\n\n\n\n");
            AddBytes(new byte[] { 0x1D, 0x56, 0x01 });     // GS V 1 = partial cut

            return buffer.ToArray();
        }

        /// <summary>
        /// Writes bytes to the USB device file.
        /// On failure, retries once after re-detecting and re-granting permission.
        /// </summary>
        private async Task WriteToDeviceAsync(byte[] data)
        {
            await Task.Run(() =>
            {
                bool TryWrite(string path)
                {
                    try
                    {
                        using var fs = new FileStream(
                            path,
                            FileMode.Open,
                            FileAccess.Write,
                            FileShare.ReadWrite);
                        fs.Write(data, 0, data.Length);
                        fs.Flush();
                        Debug.WriteLine($"[XprinterUSB] Wrote {data.Length} bytes → {path}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[XprinterUSB] Write failed ({path}): {ex.Message}");
                        return false;
                    }
                }

                // First attempt
                if (TryWrite(_detectedPath!)) return;

                // Retry: re-detect + re-chmod then try again
                Debug.WriteLine("[XprinterUSB] ⟳ Retrying after re-detect...");
                _detectedPath = DetectAndGrantPermission();
                if (_detectedPath != null)
                    TryWrite(_detectedPath);
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Debug.WriteLine("[XprinterUSB] Disposed.");
            }
        }
    }
}
