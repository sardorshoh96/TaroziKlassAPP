using System.Text;
using TaroziAPP.Services;
using TaroziAPP.Services.Api;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using ZXing.Net.Maui;

namespace TaroziAPP;

public partial class QRPaymentDialog : ContentPage
{
    private readonly string _paymentType;
    private readonly int _amount;
    private readonly string? _paymentId;
    private readonly string? _merchantId;
    private readonly string? _serviceId;
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(2);
    private DateTime _startTime;
    private Timer? _timer;
    private Timer? _checkTimer;
    private readonly PaymentService? _paymentService;
    private readonly CredentialStorageService? _credentialStorage;


    public event EventHandler<(int amount, string paymentId)>? PaymentCompleted;

    public QRPaymentDialog(string paymentType, int amount, string? paymentId = null, 
        string? merchantId = null, string? serviceId = null,
        PaymentService? paymentService = null, CredentialStorageService? credentialStorage = null)
    {
        InitializeComponent();
        _paymentType = paymentType;
        _amount = amount;
        _paymentId = paymentId;
        _merchantId = merchantId;
        _serviceId = serviceId;
        _paymentService = paymentService;
        _credentialStorage = credentialStorage;
        
        HeaderLabel.Text = $"To'lov turi: {paymentType}";
        AmountLabel.Text = $"{amount:N0} so'm";
        
        _startTime = DateTime.Now;
        
        Initialize();
    }

    private void Initialize()
    {
        // Create transaction first (same as Flutter code)
        _ = CreateTransactionAsync();
        
        // Start timer
        _timer = new Timer(UpdateTimer, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }
    
    private async Task CreateTransactionAsync()
    {
        try
        {
            if (_paymentService != null && _credentialStorage != null && !string.IsNullOrEmpty(_paymentId))
            {
                var credentials = await _credentialStorage.RetrieveAsync().ConfigureAwait(false);
                if (credentials != null)
                {
                    var result = await _paymentService.CreateTransactionAsync(
                        credentials,
                        _paymentId,
                        _amount
                    ).ConfigureAwait(false);
                    
                    if (result.IsSuccess)
                    {

                        System.Diagnostics.Debug.WriteLine($"[QRPaymentDialog] Transaction created successfully: {_amount} so'm, PaymentId: {_paymentId}");
                        
                        // Generate QR code after successful transaction (same as Flutter)
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            LoadingGrid.IsVisible = false;
                            GenerateQRCode();
                            StartPaymentChecking();
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[QRPaymentDialog] Failed to create transaction: {result.ErrorMessage}");
                        
                        // Show error in UI
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            StatusLabel.Text = $"Server xatosi: {result.ErrorMessage}";
                            StatusLabel.TextColor = Colors.Red;
                            StatusLabel.IsVisible = true;
                            LoadingGrid.IsVisible = false;
                        });
                        // Do NOT close dialog
                    }
                }
            }
            else
            {
                // If no payment service, just generate QR code (for demo/testing)
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadingGrid.IsVisible = false;
                    GenerateQRCode();
                    StartPaymentChecking();
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QRPaymentDialog] CreateTransactionAsync error: {ex.Message}");
            // Show error in UI instead of closing dialog
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusLabel.Text = $"Xatolik: {ex.Message}";
                StatusLabel.TextColor = Colors.Red;
                StatusLabel.IsVisible = true;
                LoadingGrid.IsVisible = false; // Stop loading
            });
            // Do NOT close dialog
        }
    }

    private void GenerateQRCode()
    {
        var merchantId = _merchantId ?? "";
        var paymentId = _paymentId ?? Guid.NewGuid().ToString("N")[..8];
        var serviceId = _serviceId ?? "";
        var amountTiyin = _amount * 100;

        string qrUrl = _paymentType switch
        {
            "Paycom" => $"https://checkout.paycom.uz/{Convert.ToBase64String(Encoding.UTF8.GetBytes($"m={merchantId};ac.paymentId={paymentId};a={amountTiyin}"))}",
            "Click" => $"https://my.click.uz/services/pay?service_id={serviceId}&merchant_id={merchantId}&amount={_amount}",
            "Uzum" => $"https://www.uzumbank.uz/open-service?serviceId={serviceId}&amount={amountTiyin}&paymentId={paymentId}",
            _ => ""
        };

        if (!string.IsNullOrEmpty(qrUrl))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var qrBytes = BarcodeHelper.GenerateQrCodeBytes(qrUrl, 300, 300);
                    if (qrBytes != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() => 
                        {
                            QRCodeImage.Source = ImageSource.FromStream(() => new MemoryStream(qrBytes));
                            QRCodeImage.IsVisible = true;
                            // QRCodeLabel removed from XAML
                            LoadingGrid.IsVisible = false;
                        });
                        System.Diagnostics.Debug.WriteLine($"[QRPaymentDialog] QR code generated ({qrBytes.Length} bytes): {qrUrl}");
                    }
                    else
                    {
                        throw new Exception("QR Generation returned null");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QRPaymentDialog] CRITICAL QR ERROR: {ex}");
                }
            });
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[QRPaymentDialog] QR URL is empty for payment type: {_paymentType}");
        }
    }

    private void StartPaymentChecking()
    {
        // Same as Flutter: check transaction every 1 second
        _checkTimer = new Timer(CheckPayment, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private async void CheckPayment(object? state)
    {
        // Same as Flutter: check transaction status via API (no automatic payment simulation)
        if (_paymentService == null || _credentialStorage == null || string.IsNullOrEmpty(_paymentId))
        {
            return;
        }

        try
        {
            var credentials = await _credentialStorage.RetrieveAsync().ConfigureAwait(false);
            if (credentials == null) return;

            var result = await _paymentService.CheckTransactionAsync(
                credentials,
                _paymentId
            ).ConfigureAwait(false);

            if (result.IsSuccess && result.Data != null)
            {
                // Same as Flutter: if (data.state == 2) - payment completed
                if (result.Data.State == 2)
                {
                    _checkTimer?.Dispose();
                    
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _timer?.Dispose();
                        _checkTimer?.Dispose();
                        
                        StatusLabel.Text = "✅ To'lov muvaffaqiyatli qabul qilindi!";
                        StatusLabel.IsVisible = true;
                        
                        // Don't block main thread - use Task.Run for delay
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(2000).ConfigureAwait(false);
                            MainThread.BeginInvokeOnMainThread(async () =>
                            {
                                try
                                {
                                    PaymentCompleted?.Invoke(this, (_amount, _paymentId ?? ""));
                                    await Navigation.PopModalAsync();
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[QRPaymentDialog] ❌ Navigation error: {ex.Message}");
                                }
                            });
                        });
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QRPaymentDialog] CheckPayment error: {ex.Message}");
        }
    }

    private void UpdateTimer(object? state)
    {
        var elapsed = DateTime.Now - _startTime;
        var remaining = _timeout - elapsed;

        if (remaining <= TimeSpan.Zero)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _timer?.Dispose();
                _checkTimer?.Dispose();
                await Navigation.PopModalAsync();
            });
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var minutes = remaining.Minutes.ToString("D2");
                var seconds = remaining.Seconds.ToString("D2");
                TimerLabel.Text = $"To'lov qilish uchun qolgan taxminiy vaqt: {minutes}:{seconds}";
            });
        }
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        _timer?.Dispose();
        _checkTimer?.Dispose();
        await Navigation.PopModalAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Dispose();
        _checkTimer?.Dispose();
    }
}
