namespace TaroziAPP;

public partial class AdvancedSettingsDialog : ContentPage
{
    private readonly TaskCompletionSource<AdvancedSettingsResult?> _tcs = new();

    public Task<AdvancedSettingsResult?> Result => _tcs.Task;

    public AdvancedSettingsDialog(int currentPortMode, string currentDeviceType, string? currentPrinterType = null, string? currentBillAcceptor = null, bool currentTaraEnabled = true)
    {
        InitializeComponent();

        // Mashina tarasi (standart: yoqilgan)
        TaraSwitch.IsToggled = currentTaraEnabled;

        // Kupyura qabul qilgich (NV10 default)
        var billIndex = string.IsNullOrEmpty(currentBillAcceptor) ? 0 : BillAcceptorPicker.Items.IndexOf(currentBillAcceptor);
        BillAcceptorPicker.SelectedIndex = billIndex >= 0 ? billIndex : 0;

        if (currentPortMode >= 0 && currentPortMode < PortModePicker.Items.Count)
        {
            PortModePicker.SelectedIndex = currentPortMode;
        }
        else
        {
            PortModePicker.SelectedIndex = 0; // Default to RS485
        }

        var index = DeviceTypePicker.Items.IndexOf(currentDeviceType);
        if (index < 0)
        {
            index = 0;
        }
        DeviceTypePicker.SelectedIndex = index;

        // Set printer type
        if (!string.IsNullOrEmpty(currentPrinterType))
        {
            var printerIndex = PrinterTypePicker.Items.IndexOf(currentPrinterType);
            if (printerIndex >= 0)
            {
                PrinterTypePicker.SelectedIndex = printerIndex;
            }
            else
            {
                PrinterTypePicker.SelectedIndex = 0; // Default
            }
        }
        else
        {
            PrinterTypePicker.SelectedIndex = 0; // Default
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        _tcs.TrySetResult(null);
        await Navigation.PopModalAsync();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (PortModePicker.SelectedIndex < 0 || DeviceTypePicker.SelectedIndex < 0 || PrinterTypePicker.SelectedIndex < 0)
        {
            await DisplayAlert("Xatolik", "Iltimos, barcha sozlamalarni tanlang.", "OK");
            return;
        }

        var portMode = PortModePicker.SelectedIndex;
        var deviceType = DeviceTypePicker.Items[DeviceTypePicker.SelectedIndex];
        var printerType = PrinterTypePicker.Items[PrinterTypePicker.SelectedIndex];
        var billAcceptor = BillAcceptorPicker.SelectedIndex >= 0
            ? BillAcceptorPicker.Items[BillAcceptorPicker.SelectedIndex]
            : "NV10";

        _tcs.TrySetResult(new AdvancedSettingsResult(portMode, deviceType, printerType, billAcceptor, TaraSwitch.IsToggled));
        await Navigation.PopModalAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _tcs.TrySetResult(null);
    }
}

public readonly record struct AdvancedSettingsResult(int PortMode, string DeviceType, string PrinterType, string BillAcceptor, bool TaraEnabled = true);
