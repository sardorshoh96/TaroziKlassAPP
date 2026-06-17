using System.Globalization;

namespace TaroziAPP;

public partial class ReceiptDialog : ContentPage
{
    private readonly TaskCompletionSource<bool> _closeCompletionSource = new();
    private readonly DateTime _endTime;
    private readonly double _weight;
    private readonly int _amount;
    private readonly string _address;
    private readonly DateTime _timestamp;

    public Task Completion => _closeCompletionSource.Task;

    public ReceiptDialog(double weight, int amount, string address, DateTime timestamp)
    {
        InitializeComponent();

        _weight = weight;
        _amount = amount;
        _address = string.IsNullOrWhiteSpace(address) ? "Manzil kiritilmagan" : address;
        _timestamp = timestamp;
        _endTime = DateTime.Now.AddSeconds(30);

        WeightLabel.Text = $"{_weight:0.###} kg";
        AmountLabel.Text = $"{_amount:N0} so'm";
        AddressLabel.Text = _address;
        DateLabel.Text = _timestamp.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        TimeLabel.Text = _timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        UpdateTimerLabel(_endTime - DateTime.Now);
        Device.StartTimer(TimeSpan.FromSeconds(1), OnTimerTick);
    }

    private bool OnTimerTick()
    {
        var remaining = _endTime - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            MainThread.BeginInvokeOnMainThread(async () => await CloseAsync());
            return false;
        }

        MainThread.BeginInvokeOnMainThread(() => UpdateTimerLabel(remaining));
        return true;
    }

    private void UpdateTimerLabel(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        TimerLabel.Text = $"Yopilishiga qolgan vaqt: {seconds}s";
    }

    private async Task CloseAsync()
    {
        if (_closeCompletionSource.Task.IsCompleted)
            return;

        _closeCompletionSource.TrySetResult(true);

        if (Navigation.ModalStack.Contains(this))
        {
            await Navigation.PopModalAsync();
        }
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await CloseAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _closeCompletionSource.TrySetResult(true);
    }
}

