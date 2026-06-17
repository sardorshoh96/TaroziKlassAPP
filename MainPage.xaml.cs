using TaroziAPP.Models;
using TaroziAPP.Services;
using TaroziAPP.Services.Api;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Maui.ApplicationModel;
using System.Text.Json;
using System.Linq;
using System.IO;
using Microsoft.Maui.Controls.PlatformConfiguration;
using System.Threading;



namespace TaroziAPP;
public partial class MainPage : ContentPage
{
	
    private double currentWeight = 0;
	private bool isOnline = true;
	private bool isKioskMode = true; // Enable kiosk mode by default
	private string _carNumber = ""; // Mashina davlat raqami

	
	private PaymentState paymentState = new PaymentState();
	private Models.Device? currentDevice;
	private DeviceInfoDto? _currentDeviceDto; // Store DeviceInfoDto to access Categories and Products
	private string? _cachedPhoneNumber; // Cache phone number to avoid Preferences.Get calls
	
	private readonly DeviceService _deviceService;
	private readonly PaymentService _paymentService;
	private readonly CredentialStorageService _credentialStorage;
	public readonly DeviceStorageService _deviceStorage;
	private readonly SocketNotificationService _socketService;
	private readonly LogsProvider _logsProvider;
	private readonly SendLogService _sendLogService;
	// Weight providers - port rejimiga qarab ishlatiladi
	private readonly RS485WeightProvider? _rs485WeightProvider;
	private readonly RS232WeightProvider? _rs232WeightProvider;
	private readonly EthernetWeightProvider? _ethernetWeightProvider;
	// Printer service
	private IPrinterService? _printerService;
	// NV10 USB+ Cash Validator service
	private  NV10Native  _nv10Validator;
	private List<PriceEntry> priceList = new List<PriceEntry>();
	private const string PriceListPreferenceKey = "price_list";

	private int _currentPortMode = 0; // Cached port mode (0: RS485, 1: RS232, 2: Ethernet)

	public MainPage(DeviceService deviceService, PaymentService paymentService, 
		CredentialStorageService credentialStorage, DeviceStorageService deviceStorage,
		SocketNotificationService socketService, LogsProvider logsProvider, SendLogService sendLogService)
	{
       
        try
		{
            InitializeComponent();
          
            _deviceService = deviceService;
			_paymentService = paymentService;
			_credentialStorage = credentialStorage;
			_deviceStorage = deviceStorage;
			_socketService = socketService;
			_logsProvider = logsProvider;
			_sendLogService = sendLogService;
			
			// Set up socket event handlers
			//_socketService.ConnectionStateChanged += OnSocketConnectionStateChanged;
			//_socketService.PaymentReceivedResponse += OnPaymentReceivedResponse;
			
			// Set up device refresh callback
			_sendLogService.SetOnDeviceRefreshed(OnDeviceRefreshed);

			// Check port mode (0: RS485, 1: RS232, 2: Ethernet)
			_currentPortMode = Preferences.Get("port_mode", 0);
			
			// Initialize weight providers - ikkalasi ham, port rejimiga qarab ishlatiladi
			try
			{
				_rs485WeightProvider = new RS485WeightProvider(paymentState);
				_rs485WeightProvider.WeightChanged += OnWeightChanged;
				_rs485WeightProvider.RawDataReceived += OnRawDataReceived;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] RS485WeightProvider initialization error: {ex.Message}");
			}
			
			try
			{
				_rs232WeightProvider = new RS232WeightProvider(paymentState);
				_rs232WeightProvider.WeightChanged += OnWeightChanged;
				_rs232WeightProvider.RawDataReceived += OnRawDataReceived;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] RS232WeightProvider initialization error: {ex.Message}");
			}

			try
			{
				_ethernetWeightProvider = new EthernetWeightProvider();
				_ethernetWeightProvider.WeightChanged += OnWeightChanged;
				_ethernetWeightProvider.RawDataReceived += OnRawDataReceived;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] EthernetWeightProvider initialization error: {ex.Message}");
			}
			
			// Initialize printer service
			ReloadPrinterService();

			// Initialize NV10 USB+ Cash Validator service
			try
			{
				_nv10Validator = new NV10Native();
				// Subscribe to events
				_nv10Validator.GetType().GetEvent("BillAccepted")?.AddEventHandler(_nv10Validator, new Action<int>(OnNV10BillAccepted));
                NV10Native.Start();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] CRITICAL: NV10 initialization error (likely libSerialPort.so missing): {ex.Message}");
			}

           

			
			// Load price list from Preferences (synchronous, fast)
			try
			{
				LoadPriceList();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] LoadPriceList error: {ex.Message}");
			}

			try
			{
				SetupPaymentState();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] SetupPaymentState error: {ex.Message}");
			}
			
			// Initialize providers (non-blocking) - wrapped in try-catch
			try
			{
				InitializeProviders();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] InitializeProviders error: {ex.Message}");
			}
			
			// Check credentials and load device asynchronously (non-blocking)
		
			// Don't call UpdateUI in constructor - XAML elements might not be initialized yet
			// UpdateUI will be called in OnAppearing
		}
		catch (Exception ex)
		{
			// Critical error during initialization - log and continue
			System.Diagnostics.Debug.WriteLine($"[MainPage] CRITICAL: Constructor error: {ex.Message}");
			System.Diagnostics.Debug.WriteLine($"[MainPage] Stack trace: {ex.StackTrace}");
			
		
		}
        _ = InitializeViewAsync();
    }







    private async Task InitializeViewAsync()
	{
		try
		{
			// Check app version to detect updates
			var currentVersion = AppInfo.VersionString;
			var savedVersion = Preferences.Get("app_version", "");
			var isAppUpdated = !string.IsNullOrEmpty(savedVersion) && savedVersion != currentVersion;

			if (isAppUpdated)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] App updated detected: {savedVersion} -> {currentVersion}");
			}

			// Check credentials immediately without blocking
			// Login va parol hech qachon o'zgarmaydi, faqat bir marta saqlanadi
			var credentials = await _credentialStorage.RetrieveAsync().ConfigureAwait(false);
			var savedDevice = await _deviceStorage.GetAsync().ConfigureAwait(false);

			// Load current device DTO from preferences to access categories/products on startup
			var dtoJson = Preferences.Get("device_info_dto", "");
			if (!string.IsNullOrEmpty(dtoJson))
			{
				try
				{
					_currentDeviceDto = JsonSerializer.Deserialize<DeviceInfoDto>(dtoJson);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[MainPage] Error deserializing saved device DTO: {ex.Message}");
				}
			}

			// Set currentDevice from saved device
		if (savedDevice != null)
		{
			currentDevice = savedDevice;
			System.Diagnostics.Debug.WriteLine($"[MainPage] 📱 Device yuklandi: Name='{currentDevice.Name}', ID='{currentDevice.Id}', Phone='{currentDevice.PhoneNumber}', Payments={currentDevice.Payments?.Count ?? 0}");
			
			// Load payment images from cache in background
			_ = Task.Run(async () =>
			{
				try
				{
					await LoadPaymentImagesFromCache(credentials?.Login ?? "", credentials?.Password ?? "");
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[MainPage] LoadPaymentImagesFromCache error: {ex.Message}");
				}
			});
		}
		else
		{
			System.Diagnostics.Debug.WriteLine("[MainPage] ⚠️ Saqlangan device topilmadi");
		}

		// Set credentials in services for automatic use
		_deviceService.SetCredentials(credentials?.Login ?? "", credentials?.Password ?? "");
		_paymentService.SetCredentials(credentials?.Login ?? "", credentials?.Password ?? "");
            
		if (_currentDeviceDto == null && credentials != null)
		{
			_ = Task.Run(async () =>
			{
				try
				{
					System.Diagnostics.Debug.WriteLine("[MainPage] 🔄 Device DTO is null on startup, performing initial silent refresh...");
					var refreshResult = await _deviceService.RefreshAsync(credentials).ConfigureAwait(false);
					if (refreshResult.IsSuccess && refreshResult.Data != null)
					{
						var deviceDto = refreshResult.Data;
						_currentDeviceDto = deviceDto;
						Preferences.Set("device_info_dto", JsonSerializer.Serialize(deviceDto));
						System.Diagnostics.Debug.WriteLine("[MainPage] ✅ Initial silent refresh succeeded, saved DTO.");
					}
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[MainPage] Initial silent refresh error: {ex.Message}");
				}
			});
		}
            
            // Update UI on main thread
            MainThread.BeginInvokeOnMainThread(() =>
            {
                System.Diagnostics.Debug.WriteLine("[MainPage] 🔄 UpdateUI() chaqirilmoqda...");
                UpdateUI();
                System.Diagnostics.Debug.WriteLine("[MainPage] ✅ UpdateUI() tugadi");
            });

            if (credentials == null || string.IsNullOrEmpty(credentials.Password))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Application.Current!.MainPage = App.Services.GetRequiredService<LoginPage>();
                });
                return;
            }

            // Update version if it's first run
            if (string.IsNullOrEmpty(savedVersion))
			{
				Preferences.Set("app_version", currentVersion);
			}

			System.Diagnostics.Debug.WriteLine("[MainPage] No saved credentials - login page shown");
		}
		catch (Exception ex)
		{
			
		}
	}

	



	private void InitializeProviders()
	{
		try
		{
			var deviceTypeName = Preferences.Get("device_type", "Standart");
			int baudRate = 9600;
			int dataBits = 8;
			int stopBits = 1;
			AndroidSerialPort.Parity parity = AndroidSerialPort.Parity.None;

			// Get full config from device type
			switch (deviceTypeName)
			{
				case "Standart": 
					baudRate = 9600; 
					break;
				case "A12E": 
					baudRate = 9600; 
					break;
				case "XK3118T1": 
					baudRate = 9600; 
					break;
				case "HPM-D": 
					baudRate = 9600; 
					break;
				case "TITAN H12": 
					baudRate = 9600; 
					dataBits = 8;
					stopBits = 1;
					parity = AndroidSerialPort.Parity.Even; // 8E1
					break;
				default: 
					baudRate = 9600; 
					break;
			}

			// Get port mode (0: RS485, 1: RS232, 2: Ethernet)
			int portMode = Preferences.Get("port_mode", 0); // Default to RS485

			if (portMode == 0) // RS485
			{
				_rs485WeightProvider?.Start("/dev/ttyS0", baudRate, dataBits, stopBits, parity);
				_rs232WeightProvider?.Stop();
				_ethernetWeightProvider?.Stop();
			}
			else if (portMode == 1) // RS232
			{
				_rs232WeightProvider?.Start("/dev/ttyS7", baudRate, dataBits, stopBits, parity);
				_rs485WeightProvider?.Stop();
				_ethernetWeightProvider?.Stop();
			}
			else if (portMode == 2) // Ethernet (TCP Server)
			{
				int port = Preferences.Get("device_port", 8234);
				_ethernetWeightProvider?.Start(port);
				_rs485WeightProvider?.Stop();
				_rs232WeightProvider?.Stop();
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] InitializeProviders error: {ex.Message}");
		}
	}

	/// <summary>
	/// Portdan o'qilgan raw ma'lumotni qurilma turiga yuborib qayta ishlash
	/// </summary>
	private void OnRawDataReceived(byte[] rawData)
	{
		
			var deviceTypeName = Preferences.Get("device_type", "Standart");
			int portMode = Preferences.Get("port_mode", 0); // Get current port mode
			
			// Qurilma turiga qarab class yaratish
			object? deviceInstance = deviceTypeName switch
			{
				"Standart" => new Models.DeviceTypes.StandartDevice(),
				"A12E" => new Models.DeviceTypes.A12EDevice(),
				"XK3118T1" => new Models.DeviceTypes.XK3118T1Device(),
				"HPM-D" => new Models.DeviceTypes.HPMDDevice(),
				"TITAN H12" => new Models.DeviceTypes.TITANH12Device(),
				_ => new Models.DeviceTypes.StandartDevice()
			};
			
			// Reflection orqali ProcessWeightData metodini chaqirish
			var method = deviceInstance.GetType().GetMethod("ProcessWeightData");
			if (method != null)
			{
				var result = method.Invoke(deviceInstance, new object[] { rawData });
				if (result is int weightGrams)
				{
					// Qurilma turi qayta ishlagan ma'lumotni provider'ga yuborish
					if (portMode == 0)
					{
						_rs485WeightProvider?.ProcessWeightFromDevice(weightGrams);
						System.Diagnostics.Debug.WriteLine($"[MainPage] Processed weight {weightGrams} g from {deviceTypeName} via RS485");
					}
					else if (portMode == 1)
					{
						_rs232WeightProvider?.ProcessWeightFromDevice(weightGrams);
						System.Diagnostics.Debug.WriteLine($"[MainPage] Processed weight {weightGrams} g from {deviceTypeName} via RS232");
					}
					else if (portMode == 2)
					{
						_ethernetWeightProvider?.ProcessWeightFromDevice(weightGrams);
						System.Diagnostics.Debug.WriteLine($"[MainPage] Processed weight {weightGrams} g from {deviceTypeName} via Ethernet");
					}
				}
			}
		
	}

	private void OnWeightChanged(int weight)
	{
		currentWeight = weight / 1000.0 ;// Convert from grams to kg
		
		MainThread.BeginInvokeOnMainThread(() =>
		{
			UpdateShouldPayAmount();
		});
	}

	private void OnLogAdded(Models.Logs log)
	{
		// Add log via SendLogService (it will add to LogsProvider internally)
		// Don't add twice - SendLogService.AddLog already calls _logsProvider.AddLog
		_sendLogService.AddLog(log);
		System.Diagnostics.Debug.WriteLine($"Log added: {log.PaymentId} - {log.TotalPrice}");
	}


	// LoadSavedDevice removed - InitializeViewAsync already handles this

	private void OnDeviceRefreshed(DeviceInfoDto deviceDto)
	{
		try
		{
			_currentDeviceDto = deviceDto;
			Preferences.Set("device_info_dto", JsonSerializer.Serialize(deviceDto));

			currentDevice = new Models.Device
			{
				Id = deviceDto.Id ?? "",
				Name = deviceDto.Name ?? "",
				PhoneNumber = deviceDto.ServicePhoneNumber ?? "",
				Password = deviceDto.Password ?? currentDevice?.Password ?? "",
				Payments = deviceDto.Payments?.Select(p => new Payment
				{
					Id = p.Id ?? "",
					MerchantId = p.MerchantId ?? "",
					ServiceId = p.ServiceId ?? "",
					Allow = p.Allow,
					Type = p.Type != null ? new PaymentType
					{
						Name = p.Type.Name ?? "",
						Photo = p.Type.Photo ?? ""
					} : null
				}).ToList() ?? new List<Payment>()
			};

			System.Diagnostics.Debug.WriteLine($"[MainPage] OnDeviceRefreshed: Payments={currentDevice.Payments.Count}");
			foreach (var p in currentDevice.Payments)
				System.Diagnostics.Debug.WriteLine($"[MainPage]   → {p.Type?.Name ?? "null"}");

			if (!string.IsNullOrEmpty(deviceDto.ServicePhoneNumber))
			{
				_cachedPhoneNumber = deviceDto.ServicePhoneNumber;
				Preferences.Set("device_phone_number", _cachedPhoneNumber);
			}

			// UI yangilash va rasmlarni yuklash — BARCHASI main thread da (SecureStorage xavfsiz)
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				UpdateUI();
				try
				{
					var credentials = await _credentialStorage.RetrieveAsync();
					if (credentials != null)
						await LoadPaymentImagesFromCache(credentials.Login ?? "", credentials.Password ?? "");
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[MainPage] Image reload error: {ex.Message}");
				}
			});
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] OnDeviceRefreshed error: {ex.Message}");
		}
	}

	private void OnNV10BillAccepted(int billValue)
	{
		// Amount tekshiruvi
		if (billValue <= 0)
		{
			System.Diagnostics.Debug.WriteLine($"[NV10] ⚠️ Bill accepted: amount manfiy yoki nol ({billValue})");
			return;
		}

		// CRITICAL: Balansga pul tushishi ENG MUHIM va DARHOL bo'lishi kerak
		// Hech narsa kutib qolmasligi kerak - keyingi pul tushishi o'tkazib yuborilmasligi uchun
		// Event is already invoked on main thread from NV10CashValidatorService
		paymentState.AddBankBalance(billValue);
		UpdateShouldPayAmount();
		UpdateUI(); // UI darhol yangilanadi
		
		System.Diagnostics.Debug.WriteLine($"[NV10] ✅ Balansga pul tushdi: {billValue} so'm (darhol yangilandi)");
		
		// CRITICAL: Main thread'da ma'lumotlarni capture qilish (race condition oldini olish uchun)
		// Keyin background'da ishlatish
		var capturedDevice = currentDevice; // Capture reference
		var capturedDeviceDto = _currentDeviceDto; // Capture reference
		
		// Find cash payment ID va productId main thread'da (tez va xavfsiz)
		string? cashPaymentId = null;
		if (capturedDevice?.Payments != null)
		{
			var cashPayment = capturedDevice.Payments.FirstOrDefault(p => 
				p.Type != null && 
				p.Type.Name.Equals("Cash", StringComparison.OrdinalIgnoreCase));
			cashPaymentId = cashPayment?.Id;
		}
		
		string? productId = null;
		if (capturedDeviceDto?.Categories != null && capturedDeviceDto.Categories.Count > 0)
		{
			var firstCategory = capturedDeviceDto.Categories[0];
			if (firstCategory.Products != null && firstCategory.Products.Count > 0)
			{
				productId = firstCategory.Products[0].Id;
			}
		}
		
		// Capture device info for socket (string values - thread-safe)
		var deviceId = capturedDevice?.Id ?? "";
		var deviceName = capturedDevice?.Name ?? "";
		
		// Barcha qolgan ishlarni background'da qilish (log va socket)
		// Bu balansga pul tushishini hech qachon blok qilmaydi
		_ = Task.Run(async () =>
		{
			try
			{
				// Save log (background'da, blok qilmaydi)
				if (productId != null)
				{
					var log = new Models.Logs(
						paymentId: cashPaymentId ?? "",
						productId: productId,
						time: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
						qty: 1,
						price: billValue * 100, // In tiyin
						discount: 0,
						totalPrice: billValue * 100, // In tiyin
						status: 2 // Success status
					);
					OnLogAdded(log);
					System.Diagnostics.Debug.WriteLine($"[NV10] ✅ Log saqlandi: {billValue} so'm");
				}
				
				// Send socket notification (background'da, blok qilmaydi)
				if (!string.IsNullOrEmpty(deviceId))
				{
					System.Diagnostics.Debug.WriteLine($"[NV10] 🔄 Socket orqali yuborilmoqda: DeviceId={deviceId}, PaymentId={cashPaymentId ?? ""}, Amount={billValue} so'm");
					
					await _socketService.SendPaymentReceivedAsync(
						deviceId,
						deviceName,
						cashPaymentId ?? "",
						"Cash",
						(billValue * 100).ToString() // Amount in tiyin
					).ConfigureAwait(false);
					
					System.Diagnostics.Debug.WriteLine($"[NV10] ✅ Socket orqali yuborildi: {billValue} so'm");
				}
			}
			catch (Exception ex)
			{
				// Xatolik bo'lsa ham balansga pul allaqachon tushgan
				System.Diagnostics.Debug.WriteLine($"[NV10] ⚠️ Background ishlar xatosi (balans allaqachon yangilangan): {ex.Message}");
			}
		});
	}



	private System.Threading.Timer? _uiUpdateTimer;
	private bool _uiUpdatePending = false;
	private System.Threading.Timer? _versionCheckTimer; // Timer for periodic version check
	
	private void SetupPaymentState()
	{
		paymentState.PropertyChanged += (s, e) =>
		{
			// Debounce UI updates to avoid excessive refreshes (minimal delay for responsiveness)
			if (!_uiUpdatePending)
			{
				_uiUpdatePending = true;
				_uiUpdateTimer?.Dispose();
				_uiUpdateTimer = new System.Threading.Timer(_ =>
				{
					_uiUpdatePending = false;
					MainThread.BeginInvokeOnMainThread(() =>
					{
						UpdateUI();
					});
				}, null, 5, Timeout.Infinite); // Reduced to 5ms for faster UI updates
			}
		};
		UpdateShouldPayAmount();
	}
	private async void OnPaymentCardClicked(object sender, EventArgs e)
	{
		try
		{
			if (sender is Button button && button.CommandParameter is string paymentType)
			{
				// Logic: If balance is enough, don't open payment dialog (User request)
				if (paymentState.BankBalance - paymentState.ShouldPay >= 0) return;

				var amountNeeded = Math.Max(paymentState.ShouldPay - paymentState.BankBalance, 1000);
				
			// Find payment ID and details from currentDevice.Payments by payment type name
			string? paymentId = null;
			string? merchantId = null;
			string? serviceId = null;
			if (currentDevice?.Payments != null)
			{
				var payment = currentDevice.Payments.FirstOrDefault(p => 
					p.Type != null && 
					p.Type.Name.Equals(paymentType, StringComparison.OrdinalIgnoreCase));
				paymentId = payment?.Id;
				merchantId = payment?.MerchantId;
				serviceId = payment?.ServiceId;
			}
			
			var dialog = new QRPaymentDialog(paymentType, amountNeeded, paymentId, merchantId, serviceId, _paymentService, _credentialStorage);
				dialog.PaymentCompleted += (s, args) => OnPaymentCompleted(s, args.amount, args.paymentId ?? paymentId ?? "");
				
				await Navigation.PushModalAsync(dialog);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] OnPaymentCardClicked error: {ex.Message}");
		}
	}

	private async void OnPaymentCompleted(object? sender, int amount, string paymentId = "")
	{
		try
		{
			// Amount tekshiruvi
			if (amount <= 0)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] ⚠️ OnPaymentCompleted: amount manfiy yoki nol ({amount})");
				return;
			}

			System.Diagnostics.Debug.WriteLine($"[MainPage] ✅ OnPaymentCompleted received amount: {amount} so'm");
			int oldBalance = paymentState.BankBalance;
			paymentState.AddBankBalance(amount);
			System.Diagnostics.Debug.WriteLine($"[MainPage] 💰 Balance updated: {oldBalance} -> {paymentState.BankBalance} so'm");
			
			// Force immediate UI update to be safe
			MainThread.BeginInvokeOnMainThread(() => UpdateUI());
			
			// Send socket notification and add log (same as Flutter - CreateTransaction already called in dialog)
			_ = Task.Run(async () =>
			{
				try
				{
					if (currentDevice == null)
					{
						System.Diagnostics.Debug.WriteLine("[MainPage] ⚠️ OnPaymentCompleted: currentDevice null");
						return;
					}

					if (string.IsNullOrEmpty(paymentId))
					{
						System.Diagnostics.Debug.WriteLine("[MainPage] ⚠️ OnPaymentCompleted: paymentId bo'sh");
						return;
					}

					// Find payment name from currentDevice.Payments
					string paymentName = "QR Payment";
					if (currentDevice.Payments != null)
					{
						var payment = currentDevice.Payments.FirstOrDefault(p => p.Id == paymentId);
						paymentName = payment?.Type?.Name ?? "QR Payment";
					}
					
					// Note: SendPaymentReceivedAsync handles its own connection with retries
					System.Diagnostics.Debug.WriteLine($"[MainPage] ✅ Sending paymentReceived via socket: DeviceId={currentDevice.Id}, PaymentId={paymentId}, PaymentName={paymentName}, Amount={amount} so'm ({(amount * 100)} tiyin)");
					
					await _socketService.SendPaymentReceivedAsync(
						currentDevice.Id ?? "",
						currentDevice.Name ?? "",
						paymentId,
						paymentName,
						(amount * 100).ToString() // Amount in tiyin (same as Flutter: widget.amount * 100)
					).ConfigureAwait(false);
					System.Diagnostics.Debug.WriteLine($"[MainPage] ✅ Payment notification sent via socket: {amount} so'm ({(amount * 100)} tiyin)");
					
					// Add log (same as Flutter)
					// Get productId from DeviceInfoDto (same as Flutter: device?.category.first.products.first.id)
					string? productId = null;
					if (_currentDeviceDto?.Categories != null && _currentDeviceDto.Categories.Count > 0)
					{
						var firstCategory = _currentDeviceDto.Categories[0];
						if (firstCategory.Products != null && firstCategory.Products.Count > 0)
						{
							productId = firstCategory.Products[0].Id;
						}
					}
					
					var log = new Models.Logs(
						paymentId: paymentId,
						productId: productId, // ProductId from DeviceInfoDto (same as Flutter)
						time: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
						qty: 1,
						price: amount * 100, // In tiyin (same as Flutter: widget.amount * 100)
						discount: 0,
						totalPrice: amount * 100, // In tiyin (same as Flutter: widget.amount * 100)
						status: 2 // Success status (same as Flutter)
					);
					OnLogAdded(log);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[MainPage] Socket notification error: {ex.Message}");
				}
			});
			
		
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] OnPaymentCompleted error: {ex.Message}");
		}
	}

	private async void OnConfirmPayment(object sender, EventArgs e)
	{
		try
		{
			// Summa 0 bo'lsa, tasdiqlashni to'xtatish
			if (paymentState.ShouldPay <= 0)
			{
				System.Diagnostics.Debug.WriteLine("[MainPage] ⚠️ Summa 0, tasdiqlash qilinmadi");
				return;
			}
			
			
	

			// To'langan summani va o'sha vaqtdagi aniq vaznni saqlash (ConfirmPayment dan oldin)
			int paidAmount = paymentState.ShouldPay;
			double confirmedWeight = currentWeight;
			
			// To'lovni tasdiqlash (dialog'siz, to'g'ridan-to'g'ri)
			paymentState.ConfirmPayment();
			
			// Printer qilish (ayni to'lov qilingan vaqtdagi vazn bilan)
			await PrintReceipt(confirmedWeight).ConfigureAwait(false);
			
			// ReceiptDialog ko'rsatish va socket orqali paymentReceived yuborish
			await ShowReceiptAndSendSocket(confirmedWeight, paidAmount).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
		
		}
	}
	
	private async Task PrintReceipt(double weightToPrint)
	{
		try
		{
			// Address ni olish - faqat Preferences'dan
			string address = Preferences.Get("device_address", "");
			
			// Printer qilish
			if (_printerService != null)
			{
				System.Diagnostics.Debug.WriteLine($"[PrintReceipt] 🖨️ Chek chiqarilmoqda: weight={weightToPrint} kg");
				await _printerService.PrintReceiptAsync(
					weightToPrint,
					DateTime.Now,
					address,
					_carNumber
				).ConfigureAwait(false);

				// Chek chiqarilgandan keyin mashina raqamini tozalash
				MainThread.BeginInvokeOnMainThread(() =>
				{
					if (CarNumberEntry != null)
					{
						CarNumberEntry.Text = string.Empty;
					}
					_carNumber = string.Empty;
				});
			}
			else
			{
				System.Diagnostics.Debug.WriteLine("[PrintReceipt] ⚠️ _printerService null, chek chiqarilmadi");
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Printer: Error - {ex.Message}");
		}
	}

	private void OnCarNumberChanged(object sender, TextChangedEventArgs e)
	{
		var upper = (e.NewTextValue ?? "").ToUpperInvariant();
		_carNumber = upper.Trim();

		// Entry textini katta harfga o'zgartirish (recursion oldini olish)
		if (sender is Entry entry && e.NewTextValue != upper)
		{
			entry.Text = upper;
			return;
		}

		System.Diagnostics.Debug.WriteLine($"[MainPage] Mashina raqami: {_carNumber}");
	}

	private void LoadPriceList()
	{
		var json = Preferences.Get(PriceListPreferenceKey, "");
		if (string.IsNullOrWhiteSpace(json))
		{
			// Default price list
			priceList = new List<PriceEntry>
			{
				new PriceEntry { Kilogram = 10, Price = 2000 }
			};
			return;
		}

		try
		{
			var priceDataList = JsonSerializer.Deserialize<List<JsonElement>>(json);
			priceList = new List<PriceEntry>();
			foreach (var item in priceDataList ?? new List<JsonElement>())
			{
				var kilogram = item.TryGetProperty("kilogram", out var kgProp) ? kgProp.GetDouble() : 0;
				var price = item.TryGetProperty("price", out var priceProp) ? priceProp.GetDecimal() : 0;
				priceList.Add(new PriceEntry { Kilogram = kilogram, Price = price });
			}
		}
		catch
		{
			// If deserialization fails, use default
			priceList = new List<PriceEntry>
			{
				new PriceEntry { Kilogram = 10, Price = 2000 }
			};
		}
	}

	private void UpdateShouldPayAmount()
	{
		var price = paymentState.CalculatePriceByWeight(currentWeight, priceList);
		paymentState.ShouldPay = price;
	}

	private void UpdateConnectivityStatus()
	{
		var currentAccess = Connectivity.Current.NetworkAccess;
		isOnline = currentAccess == NetworkAccess.Internet;
		System.Diagnostics.Debug.WriteLine($"[MainPage] Connectivity status: {currentAccess}, isOnline: {isOnline}");
	}
	
	private void UpdateUI()
	{
		try
		{
			// Update device info (only if changed)
			var deviceName = $"Nomi: {currentDevice?.Name ?? ""}";
			if (DeviceNameLabel.Text != deviceName)
				DeviceNameLabel.Text = deviceName;
				
			var deviceId = $"ID: {currentDevice?.Id ?? ""}";
			if (DeviceIdLabel.Text != deviceId)
				DeviceIdLabel.Text = deviceId;
			
			// Update phone number - har doim currentDevice'dan olish (update bo'lganda yangilanadi)
		string phoneText = "";
		if (currentDevice != null && !string.IsNullOrWhiteSpace(currentDevice.PhoneNumber))
		{
			// currentDevice'dan telefon raqamni olish va cache'ni yangilash
			phoneText = currentDevice.PhoneNumber;
			_cachedPhoneNumber = phoneText;
			Preferences.Set("device_phone_number", phoneText);
		}
		else if (_cachedPhoneNumber != null && !string.IsNullOrEmpty(_cachedPhoneNumber))
		{
			// Cache'dan olish
			phoneText = _cachedPhoneNumber;
		}
		
		// Set to UI label
		if (PhoneNumberLabel != null && PhoneNumberLabel.Text != phoneText)
		{
			PhoneNumberLabel.Text = phoneText;
			System.Diagnostics.Debug.WriteLine($"[MainPage] 📞 Phone number updated: {phoneText}");
		}
			
			// Update payment summary (only if changed)
			var shouldPayText = paymentState.ShouldPayText;
			if (ShouldPayLabel.Text != shouldPayText)
				ShouldPayLabel.Text = shouldPayText;
				
			var bankBalanceText = paymentState.BankBalanceText;
			if (BankBalanceLabel.Text != bankBalanceText)
				BankBalanceLabel.Text = bankBalanceText;
			
			// Update confirmation button (only if changed)
			var isCompleted = paymentState.IsCompleted;
			if (ConfirmButton.IsEnabled != isCompleted)
			{
				ConfirmButton.IsEnabled = isCompleted;
				ConfirmButton.BackgroundColor = isCompleted 
					? Color.FromArgb("#2E3192") 
					: Color.FromArgb("#9AA0FF");
			}

			// Click va Uzum kartalarini JSON Payments ro'yxatiga qarab ko'rsatish/yashirish
			UpdatePaymentCardVisibility();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] UpdateUI error: {ex.Message}");
		}
	}

		/// <summary>
	/// JSON dan kelgan Payments ro'yxatiga qarab barcha kartalarni ko'rsatadi/yashiradi.
	/// </summary>
	private void UpdatePaymentCardVisibility()
	{
		try
		{
			var payments = currentDevice?.Payments;

			System.Diagnostics.Debug.WriteLine($"[MainPage] UpdatePaymentCardVisibility: payments={payments?.Count ?? -1}");

			// allow=true bo'lgandagina ko'rsatiladi
			bool hasPaycom = payments != null &&
				payments.Any(p => p.Allow && p.Type != null &&
					p.Type.Name.Equals("Paycom", StringComparison.OrdinalIgnoreCase));

			bool hasClick = payments != null &&
				payments.Any(p => p.Allow && p.Type != null &&
					p.Type.Name.Equals("Click", StringComparison.OrdinalIgnoreCase));

			bool hasUzum = payments != null &&
				payments.Any(p => p.Allow && p.Type != null &&
					p.Type.Name.Equals("Uzum", StringComparison.OrdinalIgnoreCase));

			System.Diagnostics.Debug.WriteLine($"[MainPage] → Paycom={hasPaycom} Click={hasClick} Uzum={hasUzum}");

			PaycomCard.IsVisible = hasPaycom;
			ClickCard.IsVisible  = hasClick;
			UzumCard.IsVisible   = hasUzum;

		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] UpdatePaymentCardVisibility error: {ex.Message}");
		}
	}

	public void ReloadPrinterService()
	{
		try
		{
			var printerType = Preferences.Get("printer_type", "TG2480H");
			System.Diagnostics.Debug.WriteLine($"[MainPage] Reloading printer service. Type: {printerType}");

			// Dispose old service if necessary (assuming it might need cleanup)
			if (_printerService is IDisposable disposable)
			{
				disposable.Dispose();
			}

			if (printerType == "Cashino KP-302")
			{
				_printerService = new CashinoKP302PrinterService();
			}
			else if (printerType == "701")
			{
				_printerService = new Printer701Service();
			}
			else if (printerType == "Xprinter (USB)")
			{
				_printerService = new XprinterUsbPrinterService();
			}
			else
			{
				_printerService = new TG2480HPrinterService();
			}
			System.Diagnostics.Debug.WriteLine($"[MainPage] Printer service reloaded: {_printerService.GetType().Name}");
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] ReloadPrinterService error: {ex.Message}");
		}
	}

	protected override void OnAppearing()
	{
		try
		{
			base.OnAppearing();

            // Kiosk Mode Logic: Enter if enabled
            if (Preferences.Get("is_kiosk_mode", false))
            {
                if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    try
                    {
                        var activity = Platform.CurrentActivity;
                        if (activity != null)
                        {
                            TaroziAPP.Platforms.Android.KioskService.EnterKioskMode(activity);
                            System.Diagnostics.Debug.WriteLine("[MainPage] ✅ Kiosk mode re-entered");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainPage] ❌ Error re-entering Kiosk mode: {ex.Message}");
                    }
                }
            }
			// XAML elements are now initialized, safe to update UI
			UpdateUI();
			UpdateConnectivityStatus();
			
			// Check if port mode changed and reinitialize providers if needed
			try
			{
				var savedPortMode = Preferences.Get("port_mode", 0);
				if (savedPortMode != _currentPortMode)
				{
					_currentPortMode = savedPortMode;
					InitializeProviders();
				}
				else
				{
					// Ensure providers are running even if port mode didn't change
					try
					{
						InitializeProviders();
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[MainPage] Provider restart error in OnAppearing: {ex.Message}");
					}
					
				
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] Port mode check error in OnAppearing: {ex.Message}");
			}
			
			// Always show home view (no login page visible to user)
			
			
		
			
			// Subscribe to connectivity changes
			try
			{
				Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] Connectivity subscription error: {ex.Message}");
			}
			
			// Start periodic version check (every 5 minutes)
		
		}
		catch (Exception ex)
		{
			// Critical error - log but don't crash
			
		}
	}
	protected override void OnDisappearing()
	{
		base.OnDisappearing();
        
        // Kiosk Mode Logic: Exit when leaving MainPage (e.g. to Settings or showing Modal)
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            try
            {
                var activity = Platform.CurrentActivity;
                if (activity != null)
                {
                    TaroziAPP.Platforms.Android.KioskService.ExitKioskMode(activity);
                    System.Diagnostics.Debug.WriteLine("[MainPage] ✅ Kiosk mode exited (OnDisappearing)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] ❌ Error exiting Kiosk mode: {ex.Message}");
            }
        }
		// Unsubscribe from connectivity changes
		Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
		// Stop periodic version check
		StopPeriodicVersionCheck();
		// NOTE: Don't stop providers or dispose NV10 here - they should keep running
		// Only cleanup when app is actually closing, not when modal pages open
	}
		private void StopPeriodicVersionCheck()
	{
		try
		{
			_versionCheckTimer?.Dispose();
			_versionCheckTimer = null;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] StopPeriodicVersionCheck error: {ex.Message}");
		}
	}	
	private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
	{
		var previousOnline = isOnline;
		UpdateConnectivityStatus();
		System.Diagnostics.Debug.WriteLine($"[MainPage] Connectivity changed: {e.NetworkAccess}, isOnline: {isOnline}, previousOnline: {previousOnline}");
		
		// Agar internet yangi ulanadigan bo'lsa (offline -> online)
		if (!previousOnline && isOnline)
		{
			System.Diagnostics.Debug.WriteLine("[MainPage] ✅ Internet ulandi! Internet bilan bog'liq ishlarni ishga tushiryapman...");
			
			// Internet bilan bog'liq ishlarni ishga tushirish
			_ = Task.Run(async () =>
			{
				try
				{
					// 1. Credentials olish
					var credentials = await _credentialStorage.RetrieveAsync().ConfigureAwait(false);
					if (credentials == null)
					{
						System.Diagnostics.Debug.WriteLine("[MainPage] ⚠️ Credentials topilmadi, reconnect qilinmadi");
						return;
					}
					
					// 2. Device ma'lumotlarini yangilash (silent refresh) - Socket ulanishi olib tashlandi (faqat to'lovda ulanadi)
					try
					{
						System.Diagnostics.Debug.WriteLine("[MainPage] 🔄 Device ma'lumotlarini yangilayapman...");
						var refreshResult = await _deviceService.RefreshAsync(credentials).ConfigureAwait(false);
						if (refreshResult.IsSuccess && refreshResult.Data != null)
						{
							var deviceDto = refreshResult.Data;
							_currentDeviceDto = deviceDto;
							Preferences.Set("device_info_dto", JsonSerializer.Serialize(deviceDto));
							
							// Convert DTO to model
							var passwordFromJson = deviceDto.Password ?? credentials.Password; // Basic auth JSON'dan kelgan parolni ishlatish
							currentDevice = new Models.Device
							{
								Id = deviceDto.Id ?? "",
								Name = deviceDto.Name ?? "",
								PhoneNumber = deviceDto.ServicePhoneNumber ?? "", // ServicePhoneNumber ishlatish
								Password = passwordFromJson, // Basic auth JSON'dan kelgan parolni ishlatish
								Payments = deviceDto.Payments?.Select(p => new Payment
								{
									Id = p.Id ?? "",
									MerchantId = p.MerchantId ?? "",
									ServiceId = p.ServiceId ?? "",
									Allow = p.Allow,
									Type = p.Type != null ? new PaymentType
									{
										Name = p.Type.Name ?? "",
										Photo = p.Type.Photo ?? ""
									} : null
								}).ToList() ?? new List<Payment>()
							};
							
							// Save device to storage (parol bilan birga saqlanadi)
							await _deviceStorage.SaveAsync(currentDevice).ConfigureAwait(false);
							System.Diagnostics.Debug.WriteLine($"[MainPage] ✅ Device ma'lumotlari yangilandi va saqlandi (parol bilan birga): Password='{currentDevice.Password}'");
							
							// Update UI
							MainThread.BeginInvokeOnMainThread(() =>
							{
								UpdateUI();
							});
							
							// Load payment images - force refresh to get latest images (only if needed)
							// Internet ulanadigan bo'lsa, rasmlarni yangilash kerak bo'lishi mumkin
						
							
						
						}
						else
						{
							System.Diagnostics.Debug.WriteLine($"[MainPage] ⚠️ Device refresh failed: {refreshResult.ErrorMessage ?? "Unknown error"}");
						}
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[MainPage] ❌ Device refresh error: {ex.Message}");
					}
					
					// 4. Saqlangan loglarni yuborish (SendLogService o'zi boshqaradi, lekin tekshirib ko'ramiz)
					System.Diagnostics.Debug.WriteLine("[MainPage] ✅ Internet bilan bog'liq ishlar ishga tushirildi!");
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[MainPage] ❌ Internet reconnect error: {ex.Message}");
				}
			});
		}
		else if (previousOnline && !isOnline)
		{
			System.Diagnostics.Debug.WriteLine("[MainPage] ⚠️ Internet uzildi!");
		}
	}
	private async void OnHeaderLongPressed(object sender, EventArgs e)
	{
		try
		{
			// Show password dialog
			string enteredPassword = await DisplayPromptAsync("Sozlamalar", 
				"Parolni kiriting:", 
				"Kirish", 
				"Bekor", 
				"Parol...", 
				-1, 
				Keyboard.Default, 
				"");

			if (string.IsNullOrWhiteSpace(enteredPassword))
			{
				return; // Bekor qilindi yoki parol kiritilmadi
			}

			// Parolni tekshirish - basic auth JSON'dan kelgan parolni ishlatish (currentDevice.Password)
			
			
			// Debug: currentDevice va Password holatini ko'rsatish
			System.Diagnostics.Debug.WriteLine($"[MainPage] 🔍 Parol tekshiruvi:");
			System.Diagnostics.Debug.WriteLine($"[MainPage]   - currentDevice null: {currentDevice == null}");
			if (currentDevice != null)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage]   - currentDevice.Password: '{currentDevice.Password}'");
				System.Diagnostics.Debug.WriteLine($"[MainPage]   - currentDevice.Password length: {currentDevice.Password?.Length ?? 0}");
				System.Diagnostics.Debug.WriteLine($"[MainPage]   - currentDevice.Password IsNullOrEmpty: {string.IsNullOrEmpty(currentDevice.Password)}");
			}
			System.Diagnostics.Debug.WriteLine($"[MainPage]   - Kiritilgan parol: '{enteredPassword}'");
			System.Diagnostics.Debug.WriteLine($"[MainPage]   - Kiritilgan parol length: {enteredPassword?.Length ?? 0}");
			
			// Basic auth JSON'dan kelgan parolni tekshirish
			if (currentDevice != null && !string.IsNullOrEmpty(currentDevice.Password))
			{
				// Trim qilish va solishtirish (whitespace muammosini oldini olish uchun)
				var trimmedEnteredPassword = enteredPassword?.Trim() ?? "";
				var trimmedDevicePassword = currentDevice.Password.Trim();

                if (trimmedEnteredPassword == trimmedDevicePassword)
                {
                    // Navigate to settings page as modal with new navigation stack
                    // This creates a completely separate navigation stack that closes fully when dismissed
                    try 
                    {
                        var settingsPage = new SettingsPage();
                        var navPage = new NavigationPage(settingsPage)
                        {
                            BarBackgroundColor = Colors.White,
                            BarTextColor = Colors.Black
                        };

                        await Navigation.PushModalAsync(navPage);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainPage] CRITICAL NAVIGATION ERROR: {ex}");
                        await DisplayAlert("Xatolik", $"Sozlamalarga kirishda xatolik: {ex.Message}", "OK");
                    }
                }
                else
                {
                    await DisplayAlert("❌ Xatolik", "Noto'g'ri parol!", "OK");
                }
            }
            else
            {
                // Agar serverdan parol kelmagan bo'sa, login paroli bilan kirishga ruxsat berish (fallback)
                // Yoki xabar chiqarish. Foydalanuvchi "jsondan kelishi kerak" dedi.
                await DisplayAlert("❌ Xatolik", "Serverdan sozlamalar paroli olinmagan!", "OK");
            }
			

		
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] OnHeaderLongPressed error: {ex.Message}");
		}
	}
	public void SetKioskMode(bool enabled)
	{
		isKioskMode = enabled;
	}
	public bool IsKioskModeEnabled()
	{
		return isKioskMode;
	}
	public Models.Device? GetCurrentDevice()
	{
		return currentDevice;
	}
	public void ReloadPriceList()
	{
		LoadPriceList();
		// Update should pay amount with new price list
		UpdateShouldPayAmount();
		// Update UI
		UpdateUI();
	}

	private async Task ShowReceiptAndSendSocket(double confirmedWeight, int paidAmount)
	{
		try
		{
			// Address ni olish
			string address = Preferences.Get("device_address", "");
			
			// ReceiptDialog ko'rsatish
			var receiptDialog = new ReceiptDialog(
				confirmedWeight,
				paidAmount,
				address,
				DateTime.Now
			);
			
			await MainThread.InvokeOnMainThreadAsync(async () =>
			{
				await Navigation.PushModalAsync(receiptDialog);
			});
			
			// PaymentReceived faqat balansga pul qo'shilganda (AddBankBalance) yuboriladi
			// Bu yerda faqat chek ko'rsatiladi, balansga pul qo'shilmaydi, shuning uchun yuborilmaydi
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] ShowReceiptAndSendSocket error: {ex.Message}");
		}
	}

	/// <summary>
	/// Load payment images from cache (previously downloaded in LoginPage)
	/// </summary>
	private async Task LoadPaymentImagesFromCache(string login, string password)
	{
		try
		{
			if (currentDevice?.Payments == null)
			{
				System.Diagnostics.Debug.WriteLine("[MainPage] No payments to load images for");
				return;
			}

			System.Diagnostics.Debug.WriteLine($"[MainPage] Loading payment images from cache for {currentDevice.Payments.Count} payments");

			foreach (var payment in currentDevice.Payments)
			{
				if (payment.Type?.Photo == null || string.IsNullOrEmpty(payment.Type.Photo))
					continue;

				var photoRef = payment.Type.Photo;
			
				// Use fileRef parameter like libra project
				var imageUrl = $"https://device.radiomer.uz/rest/files?fileRef={photoRef}";	
				try
				{
					// Load from cache (or download if not cached)
					var imageSource = await AuthImageLoader.LoadImageAsync(imageUrl, login, password);
					
					if (imageSource != null)
					{
						// Set image to UI on main thread
						MainThread.BeginInvokeOnMainThread(() =>
						{
							try
							{
								// Map payment type name to image control
								if (payment.Type.Name.Equals("Paycom", StringComparison.OrdinalIgnoreCase) ||
								    payment.Type.Name.Equals("Payme", StringComparison.OrdinalIgnoreCase))
								{
									PaycomImage.Source = imageSource;
									System.Diagnostics.Debug.WriteLine("[MainPage] ✅ Paycom image loaded");
								}
								else if (payment.Type.Name.Equals("Click", StringComparison.OrdinalIgnoreCase))
								{
									ClickImage.Source = imageSource;
									System.Diagnostics.Debug.WriteLine("[MainPage] ✅ Click image loaded");
								}
								else if (payment.Type.Name.Equals("Uzum", StringComparison.OrdinalIgnoreCase))
								{
									UzumImage.Source = imageSource;
									System.Diagnostics.Debug.WriteLine("[MainPage] ✅ Uzum image loaded");
								}
							}
							catch (Exception ex)
							{
								System.Diagnostics.Debug.WriteLine($"[MainPage] UI update error for {payment.Type.Name}: {ex.Message}");
							}
						});
					}
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[MainPage] Image load error for {payment.Type.Name}: {ex.Message}");
				}
			}

			System.Diagnostics.Debug.WriteLine("[MainPage] ✅ All payment images loaded from cache");
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage] LoadPaymentImagesFromCache error: {ex.Message}");
		}
	}
}