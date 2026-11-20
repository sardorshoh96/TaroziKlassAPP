using System.Runtime.InteropServices;
using Microsoft.Maui.ApplicationModel;

namespace TaroziAPP.Services.Ccnet;

/// <summary>
/// libCcnet.so (native C++) ustidagi yupqa C# qatlami (C100 / CCNET kupyura qabul qilgich).
/// Serial port va CCNET POLL sikli native threadda ishlaydi; pul tushganda native kod
/// delegate'ni chaqiradi, biz esa <see cref="BillStacked"/> hodisasini MainThread'da uyg'otamiz.
/// CoffeeKiosk loyihasidan ko'chirilgan (ILogger o'rniga Debug.WriteLine).
/// </summary>
public sealed class NativeCcnet : IDisposable
{
    const string Lib = "libCcnet.so";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void BillStackedNative(int typeCode, long amount, IntPtr currencyUtf8);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void StateChangedNative(int state, IntPtr detailUtf8);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int Ccnet_Start(string port, int baud);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void Ccnet_Stop();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void Ccnet_SetAccepting(int enabled);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int Ccnet_GetState();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void Ccnet_RegisterBillCallback(BillStackedNative cb);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void Ccnet_RegisterStateCallback(StateChangedNative cb);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int Ccnet_GetBillTable([Out] long[] amounts, int maxCount);

    // MUHIM: delegate'larni GC yig'ib yubormasligi uchun maydonlarda saqlaymiz.
    readonly BillStackedNative _billCb;
    readonly StateChangedNative _stateCb;

    bool _started;

    public NativeCcnet()
    {
        _billCb = OnBillStackedNative;
        _stateCb = OnStateChangedNative;
    }

    /// <summary>Pul stackerga tushdi (native threaddan, MainThread'ga marshal qilinadi).</summary>
    public event EventHandler<BillStackedEventArgs>? BillStacked;

    /// <summary>Qurilma holati o'zgardi.</summary>
    public event EventHandler<CcnetStateChangedEventArgs>? StateChanged;

    public CcnetState State => (CcnetState)Ccnet_GetState();

    public async Task StartAsync(string port = "/dev/ttyS4", int baud = 9600)
    {
        if (_started)
            return;

        Ccnet_RegisterBillCallback(_billCb);
        Ccnet_RegisterStateCallback(_stateCb);

        // Ccnet_Start RESET + settle + bill table (qurilma javob bermasa ~5 s) — UI thread'ni
        // bloklamaslik uchun fon thread'da bajaramiz (aks holda ANR).
        int rc = await Task.Run(() => Ccnet_Start(port, baud)).ConfigureAwait(false);
        if (rc != 0)
        {
            System.Diagnostics.Debug.WriteLine($"[CCNET] Ccnet_Start xato: {rc} (port {port})");
            return;
        }

        _started = true;
        System.Diagnostics.Debug.WriteLine($"[CCNET] native ishga tushdi: {port}@{baud}");
    }

    public void Stop()
    {
        if (!_started)
            return;
        Ccnet_Stop();
        _started = false;
    }

    public void SetAccepting(bool enabled) => Ccnet_SetAccepting(enabled ? 1 : 0);

    /// <summary>Native jadvaldagi nominal qiymatlar (indeks = kod).</summary>
    public long[] GetBillTable()
    {
        var buf = new long[24];
        Ccnet_GetBillTable(buf, buf.Length);
        return buf;
    }

    void OnBillStackedNative(int typeCode, long amount, IntPtr currencyUtf8)
    {
        string currency = Marshal.PtrToStringUTF8(currencyUtf8) ?? "";
        var bill = new BillDenomination(typeCode, amount, currency, true);
        System.Diagnostics.Debug.WriteLine($"[CCNET] PUL QABUL QILINDI: {bill}");

        MainThread.BeginInvokeOnMainThread(() =>
            BillStacked?.Invoke(this, new BillStackedEventArgs(bill)));
    }

    void OnStateChangedNative(int state, IntPtr detailUtf8)
    {
        string detail = Marshal.PtrToStringUTF8(detailUtf8) ?? "";
        var st = (CcnetState)state;
        MainThread.BeginInvokeOnMainThread(() =>
            StateChanged?.Invoke(this, new CcnetStateChangedEventArgs(st, detail)));
    }

    public void Dispose() => Stop();
}
