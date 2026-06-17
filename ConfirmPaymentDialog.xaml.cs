using System.Timers;

namespace TaroziAPP;

public partial class ConfirmPaymentDialog : ContentPage
{
    public event EventHandler<bool>? Confirmed;
    private System.Timers.Timer? _autoCloseTimer;
    private bool _isClosed = false;

    public ConfirmPaymentDialog(double weight, int shouldPay, int paid)
    {
        InitializeComponent();
        
        WeightLabel.Text = $"{weight:F2} kg";
        ShouldPayLabel.Text = $"{shouldPay:N0} so'm";
        PaidLabel.Text = $"{paid:N0} so'm";
        
        // Start 30 second auto-close timer
        StartAutoCloseTimer();
    }

    private void StartAutoCloseTimer()
    {
        _autoCloseTimer = new System.Timers.Timer(30000); // 30 seconds
        _autoCloseTimer.Elapsed += async (sender, e) =>
        {
            _autoCloseTimer.Stop();
            if (!_isClosed)
            {
                _isClosed = true;
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    Confirmed?.Invoke(this, false);
                    await Navigation.PopModalAsync();
                });
            }
        };
        _autoCloseTimer.AutoReset = false;
        _autoCloseTimer.Start();
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        if (_isClosed) return;
        _isClosed = true;
        _autoCloseTimer?.Stop();
        Confirmed?.Invoke(this, false);
        await Navigation.PopModalAsync();
    }

    private async void OnConfirmClicked(object sender, EventArgs e)
    {
        if (_isClosed) return;
        _isClosed = true;
        _autoCloseTimer?.Stop();
        Confirmed?.Invoke(this, true);
        await Navigation.PopModalAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _autoCloseTimer?.Stop();
        _autoCloseTimer?.Dispose();
    }
}

