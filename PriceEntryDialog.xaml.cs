using TaroziAPP.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace TaroziAPP;

public partial class PriceEntryDialog : ContentPage
{
    private TaskCompletionSource<PriceEntry?> _taskCompletionSource;
    public Task<PriceEntry?> Task => _taskCompletionSource.Task;
    private readonly ObservableCollection<PriceEntry> _priceList;
    private readonly PriceEntry? _existingEntry;

    public PriceEntryDialog(ObservableCollection<PriceEntry> priceList, PriceEntry? existingEntry = null)
    {
        InitializeComponent();
        _taskCompletionSource = new TaskCompletionSource<PriceEntry?>();
        _priceList = priceList;
        _existingEntry = existingEntry;
        
        if (existingEntry != null)
        {
            // Edit mode
            HeaderLabel.Text = "Narxni tahrirlash";
            SaveButton.Text = "Saqlash";
            WeightEntry.Text = existingEntry.Kilogram.ToString("F1");
            PriceEntry.Text = existingEntry.Price.ToString("N0");
        }
        else
        {
            // Add mode
            HeaderLabel.Text = "Yangi narx qo'shish";
            SaveButton.Text = "Qo'shish";
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        _taskCompletionSource.SetResult(null);
        await Navigation.PopModalAsync();
    }

    private bool ValidatePriceOrder(double weight, decimal price)
    {
        // Create temporary list for validation
        var tempList = _priceList.ToList();
        
        // Agar edit qilayotgan bo'lsa, eski entry ni olib tashlash
        if (_existingEntry != null)
        {
            tempList.Remove(_existingEntry);
        }
        
        // Yangi yoki yangilangan entry ni qo'shish
        var tempEntry = new PriceEntry { Kilogram = weight, Price = price };
        tempList.Add(tempEntry);

        // Sort by weight
        var sortedList = tempList.OrderBy(p => p.Kilogram).ToList();

        // Validate that each item has greater weight and price than previous
        if (sortedList.Count <= 1)
            return true;

        for (int i = 1; i < sortedList.Count; i++)
        {
            var previous = sortedList[i - 1];
            var current = sortedList[i];

            // Vazn oldingisidan katta bo'lishi kerak
            if (current.Kilogram <= previous.Kilogram)
            {
                return false;
            }

            // Narx ham oldingisidan katta bo'lishi kerak
            if (current.Price <= previous.Price)
            {
                return false;
            }
        }

        return true;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        // Clear previous errors
        WeightErrorLabel.IsVisible = false;
        PriceErrorLabel.IsVisible = false;

        // Validate weight
        if (string.IsNullOrWhiteSpace(WeightEntry.Text) || !double.TryParse(WeightEntry.Text, out double weight))
        {
            WeightErrorLabel.Text = "Vaznni to'g'ri kiriting!";
            WeightErrorLabel.IsVisible = true;
            return;
        }

        if (weight <= 0)
        {
            WeightErrorLabel.Text = "Vazn 0 dan katta bo'lishi kerak!";
            WeightErrorLabel.IsVisible = true;
            return;
        }

        // Validate price
        if (string.IsNullOrWhiteSpace(PriceEntry.Text) || !decimal.TryParse(PriceEntry.Text, out decimal price))
        {
            PriceErrorLabel.Text = "Narxni to'g'ri kiriting!";
            PriceErrorLabel.IsVisible = true;
            return;
        }

        if (price <= 0)
        {
            PriceErrorLabel.Text = "Narx 0 dan katta bo'lishi kerak!";
            PriceErrorLabel.IsVisible = true;
            return;
        }

        // Validate order (weight and price must be greater than previous items)
        if (!ValidatePriceOrder(weight, price))
        {
            var isEdit = _existingEntry != null;
            string errorMessage;
            
            if (isEdit)
            {
                errorMessage = "Har bir elementdagi vazn va narx oldingisidan katta bo'lishi kerak!\n\n" +
                              "Masalan: 1 kg - 15000 so'm, 2 kg - 20000 so'm, 3 kg - 25000 so'm";
            }
            else
            {
                var lastItem = _priceList.OrderBy(p => p.Kilogram).LastOrDefault();
                errorMessage = $"Har bir elementdagi vazn va narx oldingisidan katta bo'lishi kerak!\n\n" +
                              $"Yangi elementning vazni ({weight} kg) va narxi ({price:N0} so'm) oxirgi elementdan " +
                              $"(vazn: {lastItem.Kilogram} kg, narx: {lastItem.Price:N0} so'm) katta bo'lishi kerak!";
            }

            await DisplayAlert("Xatolik", errorMessage, "OK");
            return; // Don't close dialog
        }

        // Create result
        var result = new PriceEntry
        {
            Kilogram = weight,
            Price = price
        };

        _taskCompletionSource.SetResult(result);
        await Navigation.PopModalAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_taskCompletionSource.Task.IsCompleted)
        {
            _taskCompletionSource.SetResult(null);
        }
    }
}

