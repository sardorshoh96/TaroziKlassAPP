using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using TaroziAPP.Platforms.Android;
using System;

namespace TaroziAPP;

[Activity(
    Theme = "@style/Maui.MainTheme.NoActionBar",
    MainLauncher = true
)]
[IntentFilter(
    new[] { Android.Content.Intent.ActionMain },
    Categories = new[]
    {
        Android.Content.Intent.CategoryHome,
        Android.Content.Intent.CategoryDefault
    }
)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidEnvironment.UnhandledExceptionRaiser += OnUnhandledException;
        base.OnCreate(savedInstanceState);

        try
        {
            if (KioskService.IsKioskActive(this))
            {
                KioskService.SetAsHomeApp(this);
                KioskService.EnterKioskMode(this);
                System.Diagnostics.Debug.WriteLine("[MainActivity] ✅ Kiosk mode yoqildi");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] Kiosk error: {ex.Message}");
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        try
        {
            if (KioskService.IsKioskActive(this))
            {
                KioskService.EnterKioskMode(this);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] OnResume kiosk error: {ex.Message}");
        }
    }

    private void OnUnhandledException(object? sender, RaiseThrowableEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[MainActivity] CRITICAL: {e.Exception}");
        e.Handled = true;
    }

    protected override void OnDestroy()
    {
        AndroidEnvironment.UnhandledExceptionRaiser -= OnUnhandledException;
        base.OnDestroy();
    }
}
