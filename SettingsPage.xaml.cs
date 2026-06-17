using TaroziAPP.Models;
using TaroziAPP.Platforms.Android;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Android.Content;
using Android.App;

namespace TaroziAPP;

public partial class SettingsPage : ContentPage
{

    public ObservableCollection<PriceEntry> PriceList { get; set; }
    public string WeightText { get; set; }
    public string PriceText { get; set; }
    public bool IsLastItem { get; set; }

    private bool isKioskMode = true; // Start with kiosk mode enabled
    private int portMode = 0; // 0: RS485, 1: RS232, 2: Ethernet
    private string currentAddress = "";
    private string currentDeviceType = "Standart";
    private string currentPrinterType = "USB";
    private readonly Random random = new();
    private bool isAdvancedDialogOpen;

    private const string AddressPreferenceKey = "device_address";
    private const string DeviceTypePreferenceKey = "device_type";
    private const string PortModePreferenceKey = "port_mode";
    private const string KioskModePreferenceKey = "is_kiosk_mode";
    private const string PrinterTypePreferenceKey = "printer_type";
    private const string PriceListPreferenceKey = "price_list";

    public SettingsPage()
    {
        try
        {
            InitializeComponent();
            
            // Initialize price list
            PriceList = new ObservableCollection<PriceEntry>();
            PriceListView.ItemsSource = PriceList;

            LoadSettings();
            LoadPriceList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsPage] CRITICAL CONSTRUCTOR ERROR: {ex}");
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        
        // Always exit Kiosk Mode when entering Settings
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
                System.Diagnostics.Debug.WriteLine($"[SettingsPage] ❌ Error exiting Kiosk mode: {ex.Message}");
            }
        }

        // Reload settings when page appears (in case phone number was updated after login)
        LoadSettings();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Notify MainPage to reload price list when settings page closes
        NotifyMainPagePriceListChanged();
    }

    private void NotifyMainPagePriceListChanged()
    {
        // Notify MainPage to reload price list
        if (Microsoft.Maui.Controls.Application.Current?.MainPage is NavigationPage navPage)
        {
            if (navPage.CurrentPage is MainPage mainPage)
            {
                mainPage.ReloadPriceList();
            }
            else if (navPage.RootPage is MainPage rootMainPage)
            {
                rootMainPage.ReloadPriceList();
            }
        }
        else if (Microsoft.Maui.Controls.Application.Current?.MainPage is MainPage directMainPage)
        {
            directMainPage.ReloadPriceList();
        }
    }

    private void LoadSettings()
    {
        currentAddress = Preferences.Get(AddressPreferenceKey, "");
        currentDeviceType = Preferences.Get(DeviceTypePreferenceKey, "Standart");
        currentPrinterType = Preferences.Get(PrinterTypePreferenceKey, "USB");
        portMode = Preferences.Get(PortModePreferenceKey, 0);
        isKioskMode = Preferences.Get(KioskModePreferenceKey, true);
        UpdateUI();
        UpdateIsLastFlags();
    }

    private void LoadPriceList()
    {
        var json = Preferences.Get(PriceListPreferenceKey, "");
        if (string.IsNullOrWhiteSpace(json))
        {
            // Default price list
            PriceList.Clear();
            PriceList.Add(new PriceEntry { Kilogram = 0.5, Price = 15000 });
            SavePriceList();
            return;
        }

        try
        {
            var priceDataList = JsonSerializer.Deserialize<List<JsonElement>>(json);
            PriceList.Clear();
            foreach (var item in priceDataList ?? new List<JsonElement>())
            {
                var kilogram = item.TryGetProperty("kilogram", out var kgProp) ? kgProp.GetDouble() : 0;
                var price = item.TryGetProperty("price", out var priceProp) ? priceProp.GetDecimal() : 0;
                PriceList.Add(new PriceEntry { Kilogram = kilogram, Price = price });
            }
        }
        catch
        {
            // If deserialization fails, use default
            PriceList.Clear();
            PriceList.Add(new PriceEntry { Kilogram = 0.5, Price = 15000 });
            SavePriceList();
        }
    }

    private void SavePriceList()
    {
        var priceDataList = PriceList.Select(p => new
        {
            kilogram = p.Kilogram,
            price = p.Price
        }).ToList();

        var json = JsonSerializer.Serialize(priceDataList);
        Preferences.Set(PriceListPreferenceKey, json);
    }

    private Models.Device? GetDeviceFromMainPage()
    {
        // Try different ways to get MainPage
        if (Microsoft.Maui.Controls.Application.Current?.MainPage is NavigationPage navPage)
        {
            // Try to get from root page
            if (navPage.CurrentPage is MainPage mainPage)
            {
                return mainPage.GetCurrentDevice();
            }
            
            // Try to get from root if it's MainPage
            if (navPage.RootPage is MainPage rootMainPage)
            {
                return rootMainPage.GetCurrentDevice();
            }
        }
        
        // Try direct access
        if (Microsoft.Maui.Controls.Application.Current?.MainPage is MainPage directMainPage)
        {
            return directMainPage.GetCurrentDevice();
        }
        
        return null;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        // If this page is in a modal navigation stack, close it
        var navPage = this.Parent as NavigationPage;
        var mainPage = Microsoft.Maui.Controls.Application.Current.MainPage;
        if (mainPage.Navigation.ModalStack.Contains(navPage))
        {
            await mainPage.Navigation.PopModalAsync();
            return;
        }
        
        // Fallback to regular navigation
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync();
        }
        else if (Navigation.NavigationStack.Count > 1)
        {
            await Navigation.PopAsync();
        }
    }

    private void OnKioskToggled(object sender, ToggledEventArgs e)
    {
        // Update UI immediately
        isKioskMode = e.Value;
        KioskStatusLabel.Text = isKioskMode ? "Faol" : "O'chirilgan";
        
        // Save preference (fast operation)
        Preferences.Set(KioskModePreferenceKey, isKioskMode);
        
        // Process kiosk mode change asynchronously to avoid blocking UI
        _ = ProcessKioskModeChangeAsync(isKioskMode);
    }
    
    private async Task ProcessKioskModeChangeAsync(bool enable)
    {
        // Apply kiosk mode changes on Android
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            var activity = Platform.CurrentActivity;

            if (enable)
            {
                // Enter kiosk mode
                if (KioskService.IsKioskActive(activity))
                {
                    KioskService.EnterKioskMode(activity);
                    KioskService.SetAsHomeApp(activity);
                    // Ogohlantirish olib tashlandi
                }
                else
                {
                    // Device Owner yo'q bo'lsa, switch ni qaytarish (ogohlantirishsiz)
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        // Revert switch
                        KioskSwitch.IsToggled = false;
                        isKioskMode = false;
                        Preferences.Set(KioskModePreferenceKey, false);
                        KioskStatusLabel.Text = "O'chirilgan";
                    });
                }
            }
            else
            {
                // Exit kiosk mode - do this quickly
                activity.StopLockTask();
                
                // Call exit method (may take time, but don't block UI)
                await Task.Run(() =>
                {
                    KioskService.ExitKioskMode(activity);
                });
                
                // Ogohlantirish olib tashlandi
            }
        }
        
        // Communicate kiosk mode change to MainPage
        if (Microsoft.Maui.Controls.Application.Current?.MainPage is MainPage mainPage)
        {
            mainPage.SetKioskMode(isKioskMode);
        }
    }

    private async void OnAddressClicked(object sender, EventArgs e)
    {
        string result = await DisplayPromptAsync("Manzil", 
            "Manzilni kiriting:", 
            "Saqlash", 
            "Bekor", 
            "Manzil...", 
            -1, 
            null, 
            currentAddress);

        if (!string.IsNullOrWhiteSpace(result))
        {
            currentAddress = result;
            AddressLabel.Text = currentAddress;
            AddressButton.Text = "✏️";
            Preferences.Set(AddressPreferenceKey, currentAddress);
        }
    }


    private async void OnAddPriceClicked(object sender, EventArgs e)
    {
        await ShowPriceDialog(null);
    }

    private async void OnEditPriceClicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is PriceEntry priceEntry)
        {
            await ShowPriceDialog(priceEntry);
        }
    }

    private async void OnDeletePriceClicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is PriceEntry priceEntry)
        {
            bool confirm = await DisplayAlert("O'chirish", 
                $"{priceEntry.WeightText} - {priceEntry.PriceText} ni o'chirishni xohlaysizmi?", 
                "Ha", "Yo'q");

            if (confirm)
            {
                PriceList.Remove(priceEntry);
                SavePriceList();
                UpdateEmptyState();
                UpdateIsLastFlags();
            }
        }
    }

    private async Task ShowPriceDialog(PriceEntry? existingEntry)
    {
        var dialog = new PriceEntryDialog(PriceList, existingEntry);
        await Navigation.PushModalAsync(dialog);
        
        // Wait for dialog to close and get result
        var result = await dialog.Task;

        // If user cancelled, result will be null
        if (result == null)
        {
            return;
        }

        var isEdit = existingEntry != null;
        var weight = result.Kilogram;
        var price = result.Price;

        if (isEdit && existingEntry != null)
        {
            // Update the entry
            existingEntry.Kilogram = weight;
            existingEntry.Price = price;
        }
        else
        {
            // Add new entry
            var newEntry = new PriceEntry { Kilogram = weight, Price = price };
            PriceList.Add(newEntry);
        }

        // Sort by weight
        var sortedList = PriceList.OrderBy(p => p.Kilogram).ToList();
        PriceList.Clear();
        foreach (var item in sortedList)
        {
            PriceList.Add(item);
        }

        // Save price list to Preferences
        SavePriceList();

        UpdateEmptyState();
        UpdateIsLastFlags();
    }

    private void UpdateUI()
    {
        currentAddress = Preferences.Get(AddressPreferenceKey, currentAddress);
        currentDeviceType = Preferences.Get(DeviceTypePreferenceKey, currentDeviceType);
        currentPrinterType = Preferences.Get(PrinterTypePreferenceKey, currentPrinterType);
        portMode = Preferences.Get(PortModePreferenceKey, portMode);
        isKioskMode = Preferences.Get(KioskModePreferenceKey, isKioskMode);
        
        KioskStatusLabel.Text = isKioskMode ? "Faol" : "O'chirilgan";
        KioskSwitch.IsToggled = isKioskMode;
        
        AddressLabel.Text = string.IsNullOrEmpty(currentAddress) ? "Manzil kiritilmagan" : currentAddress;
        AddressButton.Text = string.IsNullOrEmpty(currentAddress) ? "➕" : "✏️";

        // App versiyasi
        AppVersionLabel.Text = $"v{AppInfo.VersionString}";
        
        UpdateEmptyState();
        UpdateIsLastFlags();
    }

    private void UpdateEmptyState()
    {
        EmptyStateFrame.IsVisible = PriceList.Count == 0;
    }
 
    public void UpdateIsLastFlags()
    {
        for (int i = 0; i < PriceList.Count; i++)
        {
            PriceList[i].IsFirstItem = (i == 0);
            PriceList[i].IsLastItem = (i == PriceList.Count - 1);
        }
    }

    

    // Handle Android back button to close modal
    protected override bool OnBackButtonPressed()
    {
        // If this page is in a modal navigation stack, close it
        var navPage = this.Parent as NavigationPage;
        var mainPage = Microsoft.Maui.Controls.Application.Current.MainPage;
        if (mainPage.Navigation.ModalStack.Contains(navPage))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await mainPage.Navigation.PopModalAsync();
            });
            return true; // Prevent default back button behavior
        }
        
        // If there are modals in current navigation, close them first
        if (Navigation.ModalStack.Count > 0)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Navigation.PopModalAsync();
            });
            return true;
        }
        
        return base.OnBackButtonPressed();
    }

 
    public void TapGestureRecognizer_Tapped(object sender, TappedEventArgs e)
    {
        advenceSetting.IsVisible = true;
    }

    private async void OnAdvancedSettingsClicked(object sender, EventArgs e)
    {
        await OpenAdvancedSettingsAsync();
    }

    private async Task OpenAdvancedSettingsAsync()
    {
        if (isAdvancedDialogOpen)
            return;

        isAdvancedDialogOpen = true;

        var dialog = new AdvancedSettingsDialog(portMode, currentDeviceType, currentPrinterType);
        await Navigation.PushModalAsync(dialog);
        var result = await dialog.Result;

        if (result.HasValue)
        {
            var oldPortMode = portMode;
            portMode = result.Value.PortMode;
            currentDeviceType = result.Value.DeviceType;
            currentPrinterType = result.Value.PrinterType;

            Preferences.Set(PortModePreferenceKey, portMode);
            Preferences.Set(DeviceTypePreferenceKey, currentDeviceType);
            Preferences.Set(PrinterTypePreferenceKey, currentPrinterType);
            
            // If port mode or printer type changed, notify MainPage to reinitialize
            if (oldPortMode != portMode || result.Value.PrinterType != currentPrinterType)
            {
                // Notify MainPage to reinitialize
                if (Microsoft.Maui.Controls.Application.Current?.MainPage is NavigationPage navPage)
                {
                    if (navPage.CurrentPage is MainPage mainPage)
                    {
                        mainPage.ReloadPrinterService();
                        UpdateUI();
                    }
                    else if (navPage.RootPage is MainPage rootMainPage)
                    {
                        rootMainPage.ReloadPrinterService();
                        UpdateUI();
                    }
                }
                else if (Microsoft.Maui.Controls.Application.Current?.MainPage is MainPage directMainPage)
                {
                    directMainPage.ReloadPrinterService();
                    UpdateUI();
                }
            }
        }
        
        isAdvancedDialogOpen = false;
    }
}
