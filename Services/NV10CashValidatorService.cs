using Android.Runtime;
using Microsoft.Maui.ApplicationModel;
using System;
using System.Runtime.InteropServices;

namespace TaroziAPP
{
  
    public  class NV10Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void BillAcceptedCallback(int sum);

        private static BillAcceptedCallback _callback = null!;

        [DllImport("libSerialPort.so", CallingConvention = CallingConvention.Cdecl)]
        private static extern void NV10_Start();

        [DllImport("libSerialPort.so", CallingConvention = CallingConvention.Cdecl)]
        private static extern void NV10_Stop();

        [DllImport("libSerialPort.so", CallingConvention = CallingConvention.Cdecl)]
        private static extern void NV10_RegisterCallback(BillAcceptedCallback cb);

        public static event Action<int>? BillAccepted;

        public static void Start()
        {
           
                if (_callback == null)
                    _callback = OnBillFromNative;
            

            NV10_RegisterCallback(_callback);
            NV10_Start();

        }

        public static void Stop()
        {
            NV10_Stop();
        }

        private static void OnBillFromNative(int sum)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BillAccepted?.Invoke(sum);
            });
        }
    }
}
