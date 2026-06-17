using TaroziAPP.Models;

namespace TaroziAPP.Services;

public sealed class CredentialStorageService
{
    private const string LoginKey = "device_login";
    private const string PasswordKey = "device_password";

    public async Task SaveAsync(UserCredentials credentials)
    {
        await SecureStorage.Default.SetAsync(LoginKey, credentials.Login);
        await SecureStorage.Default.SetAsync(PasswordKey, credentials.Password);
    }

    public async Task<UserCredentials?> RetrieveAsync()
    {
        var login = await SecureStorage.Default.GetAsync(LoginKey);
        var password = await SecureStorage.Default.GetAsync(PasswordKey);

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return new UserCredentials(login, password);
    }

    public void Clear()
    {
        SecureStorage.Default.Remove(LoginKey);
        SecureStorage.Default.Remove(PasswordKey);
    }
}

