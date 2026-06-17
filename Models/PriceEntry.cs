using System.ComponentModel;

namespace TaroziAPP.Models
{
    public class PriceEntry : INotifyPropertyChanged
    {
        private double _kilogram;
        private decimal _price;
        private bool _isLastItem;
        private bool _isFirstItem;

        public double Kilogram
        {
            get => _kilogram;
            set
            {
                _kilogram = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WeightText));
            }
        }

        public decimal Price
        {
            get => _price;
            set
            {
                _price = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriceText));
            }
        }

        public string WeightText => $"{Kilogram:F1} kg";
        public string PriceText => $"{Price:N0} so'm";

        public bool IsLastItem
        {
            get => _isLastItem;
            set
            {
                _isLastItem = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDeletable));
            }
        }

        public bool IsFirstItem
        {
            get => _isFirstItem;
            set
            {
                _isFirstItem = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDeletable));
            }
        }

        public bool IsDeletable => IsLastItem && !IsFirstItem;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
