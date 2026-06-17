using System;
using System.Runtime.InteropServices;

namespace TaroziAPP.Services
{
    public static class AndroidSerialPort
    {
        private const string LibraryName = "libSerialPort.so";

        [DllImport(LibraryName, EntryPoint = "OpenSerialPort")]
        private static extern int OpenSerialPort(string portName);

        [DllImport(LibraryName, EntryPoint = "OpenSerialPortWithBaud")]
        private static extern int OpenSerialPortWithBaud(string portName, int baudRate);

        [DllImport(LibraryName, EntryPoint = "OpenSerialPortFull")]
        private static extern int OpenSerialPortFull(string portName, int baudRate, int dataBits, int stopBits, int parity);

        public enum Parity { None = 0, Odd = 1, Even = 2, Mark = 3, Space = 4 }

        [DllImport(LibraryName, EntryPoint = "WriteToSerialPort", CallingConvention = CallingConvention.Cdecl)]
        private static extern void WriteToSerialPort(
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data,
            int length,
            int serial_port);

        [DllImport(LibraryName, EntryPoint = "CloseSerialPortFd")]
        private static extern void CloseSerialPortFd(int serial_port);

        [DllImport(LibraryName, EntryPoint = "ReadFromSerialPort", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ReadFromSerialPort(out int length, int fd);

        // ================= OPEN =================

        public static int Open(string portName, int baudRate, int dataBits = 8, int stopBits = 1, Parity parity = Parity.None)
        {
            try
            {
                int fd = OpenSerialPortFull(portName, baudRate, dataBits, stopBits, (int)parity);
                System.Diagnostics.Debug.WriteLine($"[AndroidSerialPort] Open fd={fd} (Baud={baudRate}, Data={dataBits}, Stop={stopBits}, Parity={parity})");
                return fd;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidSerialPort] Open error: {ex.Message}");
                return -1;
            }
        }

        // ================= CLOSE =================

        public static void CloseFd(int fd)
        {
            if (fd >= 0)
            {
                try { CloseSerialPortFd(fd); }
                catch { }
            }
        }

        // ================= WRITE =================

        public static void Write(byte[] data, int fd)
        {
            if (fd < 0 || data == null || data.Length == 0)
                return;

            try
            {
                WriteToSerialPort(data, data.Length, fd);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidSerialPort] Write error: {ex.Message}");
            }
        }

        // ================= READ (FIXED) =================

        public static byte[]? Read(int fd)
        {
            if (fd < 0)
                return null;

            try
            {
                int length;
                IntPtr ptr = ReadFromSerialPort(out length, fd);

                if (ptr == IntPtr.Zero || length <= 0)
                    return null;

                byte[] buffer = new byte[length];
                Marshal.Copy(ptr, buffer, 0, length);

             

                
                string hex = BitConverter.ToString(buffer).Replace("-", " ");
                System.Diagnostics.Debug.WriteLine(
                    $"[AndroidSerialPort] ✅ Valid data: {hex} ({buffer.Length} bytes)");

                return buffer;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidSerialPort] Read error: {ex.Message}");
                return null;
            }
        }
    }
}
