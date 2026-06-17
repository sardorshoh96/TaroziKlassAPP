namespace TaroziAPP;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        Services = services;

        var splashPage = Services.GetRequiredService<SplashPage>();
        MainPage = splashPage;
    }
}




