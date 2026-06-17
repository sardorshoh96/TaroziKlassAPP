using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if ANDROID
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
#endif

namespace TaroziAPP.Platforms.Android
{
    /// <summary>
    /// Android USB Host API orqali Xprinter (Printer-80) bilan ishlaydi.
    /// Root talab qilmaydi. UsbManager → UsbDevice → bulkTransfer() ESC/POS.
    /// Vendor ID: 0x1fc9 (8137), Product ID: 0x2016 (8214)
    /// </summary>
    public class AndroidUsbPrinterService : IDisposable
    {
#if ANDROID
        // Xprinter Printer-80 identifiers (confirmed via ADB scan)
        private const int VendorId  = 0x1fc9; // 8137
        private const int ProductId = 0x2016; // 8214

        // USB Printer class code
        private const int UsbClassPrinter = 7;

        // Bulk transfer timeout (ms)
        private const int TransferTimeout = 5000;

        private UsbManager?         _usbManager;
        private UsbDevice?          _device;
        private UsbDeviceConnection? _connection;
        private UsbEndpoint?        _bulkOut;
        private bool                _disposed;

        // ─────────────────────────────────────────────────────────────────
        // Init
        // ─────────────────────────────────────────────────────────────────

        public AndroidUsbPrinterService()
        {
            _usbManager = (UsbManager?)global::Android.App.Application.Context
                .GetSystemService(Context.UsbService);
        }

        // ─────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Printer topib ulanadi. true — muvaffaqiyatli.
        /// </summary>
        public bool Connect()
        {
            try
            {
                Disconnect();

                _device = FindPrinter();
                if (_device == null)
                {
                    Debug.WriteLine("[AndroidUSBPrinter] ❌ Xprinter topilmadi (vendor=1fc9)");
                    return false;
                }

                Debug.WriteLine($"[AndroidUSBPrinter] ✅ Printer topildi: {_device.ProductName} ({_device.VendorId:X4}:{_device.ProductId:X4})");

                // USB ruxsatini tekshirish
                if (!_usbManager!.HasPermission(_device))
                {
                    Debug.WriteLine("[AndroidUSBPrinter] ⚠️ USB ruxsati yo'q, so'ralmoqda...");
                    RequestPermission(_device);
                    // Ruxsat dialogi async — birinchi marta ishlamaydi, foydalanuvchi OK bossin
                    return false;
                }

                // Interface va endpoint topish
                var (iface, endpoint) = FindPrinterEndpoint(_device);
                if (iface == null || endpoint == null)
                {
                    Debug.WriteLine("[AndroidUSBPrinter] ❌ Printer interface/endpoint topilmadi");
                    return false;
                }

                // Ulanish ochish
                _connection = _usbManager.OpenDevice(_device);
                if (_connection == null)
                {
                    Debug.WriteLine("[AndroidUSBPrinter] ❌ UsbDeviceConnection ocholmadi");
                    return false;
                }

                bool claimed = _connection.ClaimInterface(iface, true);
                _bulkOut = endpoint;

                Debug.WriteLine($"[AndroidUSBPrinter] ✅ Ulandi. Interface claimed={claimed}, BulkOut ep={_bulkOut.Address}");
                return claimed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AndroidUSBPrinter] Connect error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ESC/POS byte array ni printerga yuboradi.
        /// </summary>
        public bool Write(byte[] data)
        {
            if (_connection == null || _bulkOut == null)
            {
                Debug.WriteLine("[AndroidUSBPrinter] ❌ Write: ulanmagan");
                return false;
            }

            try
            {
                // Katta dataларni chunk qilib yuborish (USB buffer limitini hisobga olish)
                const int chunkSize = 16384; // 16KB chunks
                int offset = 0;
                int total  = data.Length;

                while (offset < total)
                {
                    int len = Math.Min(chunkSize, total - offset);
                    var chunk = new byte[len];
                    Array.Copy(data, offset, chunk, 0, len);

                    int sent = _connection.BulkTransfer(_bulkOut, chunk, len, TransferTimeout);
                    if (sent < 0)
                    {
                        Debug.WriteLine($"[AndroidUSBPrinter] ❌ BulkTransfer failed at offset {offset}");
                        return false;
                    }

                    offset += sent;
                    Debug.WriteLine($"[AndroidUSBPrinter] ✉️ Sent {sent} bytes (total {offset}/{total})");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AndroidUSBPrinter] Write error: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                _connection?.Close();
            }
            catch { }
            _connection = null;
            _bulkOut     = null;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Disconnect();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// UsbManager device ro'yxatidan Xprinter ni topadi (vendor=1fc9).
        /// </summary>
        private UsbDevice? FindPrinter()
        {
            if (_usbManager?.DeviceList == null) return null;

            foreach (var dev in _usbManager.DeviceList.Values)
            {
                Debug.WriteLine($"[AndroidUSBPrinter] USB device: {dev.ProductName} vid={dev.VendorId:X4} pid={dev.ProductId:X4}");

                // Vendor + Product ID bo'yicha aniq moslik
                if (dev.VendorId == VendorId && dev.ProductId == ProductId)
                    return dev;

                // Yoki Printer class bo'yicha (fallback)
                for (int i = 0; i < dev.InterfaceCount; i++)
                {
                    var iface = dev.GetInterface(i);
                    if (iface != null && iface.InterfaceClass == UsbClass.Printer)
                    {
                        Debug.WriteLine($"[AndroidUSBPrinter] Printer class device found: {dev.ProductName}");
                        return dev;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Printer interface (class=07) va Bulk-OUT endpoint ni topadi.
        /// </summary>
        private static (UsbInterface? iface, UsbEndpoint? endpoint) FindPrinterEndpoint(UsbDevice device)
        {
            for (int i = 0; i < device.InterfaceCount; i++)
            {
                var iface = device.GetInterface(i);
                if (iface == null) continue;

                // Printer class yoki har qanday interface (ba'zi printer driverlar class=ff ishlatadi)
                bool isPrinterClass = iface.InterfaceClass == UsbClass.Printer ||
                                      (int)iface.InterfaceClass == 0xFF ||
                                      (int)iface.InterfaceClass == 0x00;

                for (int j = 0; j < iface.EndpointCount; j++)
                {
                    var ep = iface.GetEndpoint(j);
                    if (ep == null) continue;

                    // Bulk OUT endpoint
                    if (ep.Type == UsbAddressing.XferBulk &&
                        ep.Direction == UsbAddressing.Out)
                    {
                        Debug.WriteLine($"[AndroidUSBPrinter] Found BulkOut ep={ep.Address} on interface {i} (class={(int)iface.InterfaceClass})");
                        return (iface, ep);
                    }
                }
            }
            return (null, null);
        }

        /// <summary>
        /// Android USB permission dialog ko'rsatadi.
        /// Foydalanuvchi OK bosgandan keyin Connect() qayta chaqirilishi kerak.
        /// </summary>
        private void RequestPermission(UsbDevice device)
        {
            try
            {
                var context = global::Android.App.Application.Context;
                var permissionIntent = PendingIntent.GetBroadcast(
                    context,
                    0,
                    new Intent("com.taroziapp.USB_PERMISSION"),
                    PendingIntentFlags.Mutable);

                _usbManager?.RequestPermission(device, permissionIntent);
                Debug.WriteLine("[AndroidUSBPrinter] 🔔 USB permission dialog ko'rsatildi");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AndroidUSBPrinter] RequestPermission error: {ex.Message}");
            }
        }
#endif
    }
}
