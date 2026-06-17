using Microsoft.Extensions.Logging;
using TaroziAPP.Services;

namespace TaroziAPP;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // Register services
        builder.Services.AddSingleton<JsonRpcClient>(sp => new JsonRpcClient("https://device.radiomer.uz/api/device/v1"));
        builder.Services.AddSingleton<DeviceService>();
        builder.Services.AddSingleton<PaymentService>();
        builder.Services.AddSingleton<CredentialStorageService>();
        builder.Services.AddSingleton<DeviceStorageService>();
        builder.Services.AddSingleton<SocketNotificationService>();
        builder.Services.AddSingleton<LogsProvider>();
        builder.Services.AddSingleton<SendLogService>();
        builder.Services.AddSingleton<AppUpdateService>();
       

        // Register pages
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<SplashPage>();




        // Use the App class
        builder.UseMauiApp<App>();

        // Optional: logging
#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // AppUpdateService ni ishga tushurish — har 1 soatda GitHub tekshiriladi
        app.Services.GetRequiredService<AppUpdateService>().StartPeriodicCheck();

        return app;
    }
}
