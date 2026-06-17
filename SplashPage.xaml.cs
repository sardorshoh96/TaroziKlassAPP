using TaroziAPP.Services;

namespace TaroziAPP;

public partial class SplashPage : ContentPage
{
    private readonly CredentialStorageService _credentialStorage;

    public SplashPage(CredentialStorageService credentialStorage)
    {
        InitializeComponent();
        _credentialStorage = credentialStorage;
    }

   protected override async void OnAppearing()
{
    base.OnAppearing();

    // 🔑 Android to'liq tayyor bo'lishi uchun
    await Task.Delay(500);

    var credentials = await _credentialStorage.RetrieveAsync();
    
    if (credentials != null && !string.IsNullOrEmpty(credentials.Login) && !string.IsNullOrEmpty(credentials.Password))
    {
        GoToMainPage();
    }
    else
    {
        GoToLoginPage();
    }
}

    private void GoToLoginPage()

    {
        Preferences.Remove("loginPage");

        var loginPage = App.Services.GetRequiredService<LoginPage>();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Application.Current!.MainPage = loginPage;
               
        });
    }

    private void GoToMainPage()
    {
        Preferences.Remove("mainPage");

        var mainPage = App.Services.GetRequiredService<MainPage>();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Application.Current!.MainPage = mainPage;
                
        });
    }

}
