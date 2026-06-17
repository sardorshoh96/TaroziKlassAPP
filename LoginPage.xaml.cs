using Android.App;
using Android.Content;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using TaroziAPP.Models;
using TaroziAPP.Platforms.Android;
using TaroziAPP.Services;
using TaroziAPP.Services.Api;

namespace TaroziAPP
{
    public partial class LoginPage : ContentPage
    {
        private bool isOnline = true;
        public readonly DeviceStorageService _deviceStorage;

        private Models.Device currentDevice;
        private string _cachedPhoneNumber;
        private CredentialStorageService _credentialStorage;
        private DeviceService _deviceService;
        private PaymentService _paymentService;
        private SocketNotificationService _socketService;
        private DeviceInfoDto? _currentDeviceDto;

        public LoginPage(
            DeviceStorageService deviceStorage, 
            CredentialStorageService credentialStorage, 
            DeviceService deviceService, 
            PaymentService paymentService, 
            SocketNotificationService socketService)
        {
            InitializeComponent();
            
            _deviceStorage = deviceStorage;
            _credentialStorage = credentialStorage;
            _deviceService = deviceService;
            _paymentService = paymentService;
            _socketService = socketService;
        }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        
        // Ensure Kiosk Mode is OFF when on Login Page
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            try
            {
                var activity = Platform.CurrentActivity;
                if (activity != null)
                {
                    TaroziAPP.Platforms.Android.KioskService.ExitKioskMode(activity);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoginPage] ❌ Error exiting Kiosk mode: {ex.Message}");
            }
        }
    }

    private void OnPasswordVisibilityToggled(object sender, EventArgs e)
        {
            // Toggle password visibility
            PasswordEntry.IsPassword = !PasswordEntry.IsPassword;

            // Update button text based on visibility
            PasswordVisibilityButton.Text = PasswordEntry.IsPassword ? "👁️" : "👁️‍🗨️";
        }

        private void UpdateConnectivityStatus()
        {
            var currentAccess = Connectivity.Current.NetworkAccess;
            isOnline = currentAccess == NetworkAccess.Internet;
         
        }


        private async Task PerformLogin(string login, string password)
        {
            // Check connectivity before attempting login
            UpdateConnectivityStatus();

            // If offline and we have saved device, use it
            if (!isOnline)
            {
                var savedDevice = await _deviceStorage.GetAsync();
                if (savedDevice != null)
                {
                    currentDevice = savedDevice;
                    currentDevice.Password = password;

                    // Store credentials
                    var credentials = new UserCredentials(login, password);
                    await _credentialStorage.SaveAsync(credentials);

                    // Switch to home view
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        ShowHomeView();
                        LoginButton.Text = "🔑 Tizimga kirish";
                        LoginButton.IsEnabled = true;
                    });

                    System.Diagnostics.Debug.WriteLine("[MainPage] Offline login: Using saved device data");
                    return;
                }
                else
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        ErrorLabel.Text = "❌ Internet ulanmagan. Avval tizimga kirish kerak.";
                        ErrorLabel.IsVisible = true;
                        LoginButton.Text = "🔑 Tizimga kirish";
                        LoginButton.IsEnabled = true;
                    });
                    return;
                }
            }

            var result = await _deviceService.LoginAsync(login, password);

            if (result.IsSuccess && result.Data != null)
            {
                var deviceDto = result.Data;

                // Store DeviceInfoDto to access Categories and Products
                _currentDeviceDto = deviceDto;
                Preferences.Set("device_info_dto", JsonSerializer.Serialize(deviceDto));

                // Set credentials in services for automatic use
                _deviceService.SetCredentials(login, password);
                _paymentService.SetCredentials(login, password);



                // Convert DTO to model
                var passwordFromJson = deviceDto.Password; // Basic auth JSON'dan kelgan parolni ishlatish
                currentDevice = new Models.Device
                {
                    Id = deviceDto.Id ?? "",
                    Name = deviceDto.Name ?? "",
                    PhoneNumber = deviceDto.ServicePhoneNumber ?? "", // ServicePhoneNumber ishlatish
                    Password = passwordFromJson // Basic auth JSON'dan kelgan parol (null bo'lishi mumkin)
                };

                System.Diagnostics.Debug.WriteLine($"[MainPage] ✅ Parol basic auth JSON'dan olingan: '{passwordFromJson}' (deviceDto.Password: '{deviceDto.Password ?? "null"}', fallback: '{password}')");

                // Save service phone number from server to preferences and cache
                if (!string.IsNullOrWhiteSpace(deviceDto.ServicePhoneNumber))
                {
                    _cachedPhoneNumber = deviceDto.ServicePhoneNumber;
                    Preferences.Set("device_phone_number", _cachedPhoneNumber);
                }

                // Convert payments
                currentDevice.Payments = deviceDto.Payments?.Select(p => new Payment
                {
                    Id = p.Id ?? "",
                    MerchantId = p.MerchantId ?? "",
                    ServiceId = p.ServiceId ?? "",
                    Type = p.Type != null ? new PaymentType
                    {
                        Name = p.Type.Name ?? "",
                        Photo = p.Type.Photo ?? ""
                    } : null
                }).ToList() ?? new List<Payment>();

                // Store credentials (login va parol hech qachon o'zgarmaydi, faqat bir marta saqlanadi)
                // Agar credentials allaqachon saqlangan bo'lsa, qayta saqlash shart emas
                var existingCredentials = await _credentialStorage.RetrieveAsync().ConfigureAwait(false);
                UserCredentials credentials;
                if (existingCredentials == null ||
                    existingCredentials.Login != login ||
                    existingCredentials.Password != password)
                {
                    credentials = new UserCredentials(login, password);
                    await _credentialStorage.SaveAsync(credentials);
                    System.Diagnostics.Debug.WriteLine("[MainPage] Credentials saved (first time or changed)");
                }
                else
                {
                    credentials = existingCredentials;
                    System.Diagnostics.Debug.WriteLine("[MainPage] Credentials already saved, skipping save");
                }

                // Save device to storage
                System.Diagnostics.Debug.WriteLine($"[LoginPage] 💾 Device saqlanmoqda: Name='{currentDevice.Name}', ID='{currentDevice.Id}', Phone='{currentDevice.PhoneNumber}', Payments={currentDevice.Payments?.Count ?? 0}");
                await _deviceStorage.SaveAsync(currentDevice);
                System.Diagnostics.Debug.WriteLine("[LoginPage] ✅ Device saqlandi!");

                // Update app version after successful login (ensures version is saved after update)
                var currentVersion = AppInfo.VersionString;
                Preferences.Set("app_version", currentVersion);
                System.Diagnostics.Debug.WriteLine($"[MainPage] App version saved: {currentVersion}");

                // Switch to home view IMMEDIATELY (don't wait for images)
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ShowHomeView();

                    // Reset login button text and enable state
                    LoginButton.Text = "🔑 Tizimga kirish";
                    LoginButton.IsEnabled = true;
                });

                // Load payment images in background (non-blocking) - force refresh to get latest images
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await LoadPaymentImages(login, password, forceRefresh: true).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainPage] LoadPaymentImages error in PerformLogin: {ex.Message}");
                    }
                });

                // Connect to socket in background (non-blocking)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // GitHub'dan klonlangan Flutter kodida Socket.IO URL: https://device.radiomer.uz
                        // Manba: libra/lib/provider/socket_service_provider.dart
                        var socketUrl = "https://device.radiomer.uz";
                        System.Diagnostics.Debug.WriteLine($"[MainPage] 🔌 Socket ulanishga urinmoqda: {socketUrl}");
                        await _socketService.ConnectAsync(
                            socketUrl,
                            login,
                            password
                        ).ConfigureAwait(false);

                        if (_socketService.IsConnected)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MainPage] ✅ Socket muvaffaqiyatli ulandi!");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[MainPage] ❌ Socket ulanmadi!");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainPage] ❌ Socket connection error: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MainPage] Inner exception: {ex.InnerException.Message}");
                        }
                    }
                });

                // Notify server that data has been synchronized
                await NotifyDataSynchronized(credentials).ConfigureAwait(false);
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var errorMsg = result.ErrorMessage ?? "Noto'g'ri login yoki parol!";
                    if (result.IsSuccess && result.Data == null)
                    {
                        errorMsg = "Serverdan ma'lumot kelmadi. Qayta urinib ko'ring.";
                    }
                    ErrorLabel.Text = $"❌ {errorMsg}";
                    ErrorLabel.IsVisible = true;
                    LoginButton.Text = "🔑 Tizimga kirish";
                    LoginButton.IsEnabled = true;
                });
            }
        }




        private async void OnLoginClicked(object sender, EventArgs e)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(UsernameEntry.Text) || string.IsNullOrWhiteSpace(PasswordEntry.Text))
                {
                    ErrorLabel.Text = "❌ Login va parolni kiriting!";
                    ErrorLabel.IsVisible = true;
                    return;
                }

                // Show loading state
                LoginButton.Text = "⏳ Tekshirilmoqda...";
                LoginButton.IsEnabled = false;
                ErrorLabel.IsVisible = false;

                await PerformLogin(UsernameEntry.Text.Trim(), PasswordEntry.Text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] OnLoginClicked error: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ErrorLabel.Text = $"❌ Xatolik: {ex.Message}";
                    ErrorLabel.IsVisible = true;
                    LoginButton.Text = "🔑 Tizimga kirish";
                    LoginButton.IsEnabled = true;
                });
            }
        }

        private void ShowHomeView()
        {
           
                Preferences.Remove("mainPage");

                var mainPage = App.Services.GetRequiredService<MainPage>();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Microsoft.Maui.Controls.Application.Current!.MainPage = mainPage;
                     
                });
          
        }

        private async Task LoadPaymentImages(string login, string password, bool forceRefresh)
        {
            try
            {
                if (_currentDeviceDto == null || _currentDeviceDto.Payments == null)
                {
                    System.Diagnostics.Debug.WriteLine("[LoginPage] No payments to load images for");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[LoginPage] Loading payment images for {_currentDeviceDto.Payments.Count} payments");

                // Load all payment images in parallel
                var tasks = new List<Task>();
                foreach (var payment in _currentDeviceDto.Payments)
                {
                    if (payment.Type?.Photo != null && !string.IsNullOrEmpty(payment.Type.Photo))
                    {
                        				var photoRef = payment.Type.Photo;
				
				// Use fileRef parameter like libra project
				var imageUrl = $"https://device.radiomer.uz/rest/files?fileRef={photoRef}";

                        // Load image (will cache automatically)
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var imageSource = await AuthImageLoader.LoadImageAsync(imageUrl, login, password);
                                if (imageSource != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[LoginPage] ✅ Image loaded and cached: {payment.Type.Name}");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[LoginPage] ⚠️ Image load error for {payment.Type.Name}: {ex.Message}");
                            }
                        }));
                    }
                }

                // Wait for all images to be loaded
                await Task.WhenAll(tasks).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine("[LoginPage] ✅ All payment images loaded and cached");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoginPage] LoadPaymentImages error: {ex.Message}");
            }
        }

        private async Task NotifyDataSynchronized(UserCredentials credentials)
        {
            await Task.CompletedTask;
        }
    }
}
