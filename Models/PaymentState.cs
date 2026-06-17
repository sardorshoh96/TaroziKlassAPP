using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaroziAPP.Models
{
    public class PaymentState : INotifyPropertyChanged
    {
        private int _shouldPay;
        private int _bankBalance;

        public int ShouldPay
        {
            get => _shouldPay;
            set
            {
                _shouldPay = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(ShouldPayText));
            }
        }

        public int BankBalance
        {
            get => _bankBalance;
            set
            {
                _bankBalance = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(BankBalanceText));
            }
        }

        // BALANS_CHECK_OFF - Vaqtincha o'chirilgan (tiklash uchun quyidagi qatorni yoching va ustidagini o'chiring)
        public bool IsCompleted => ShouldPay > 0; // Balans tekshiruvsiz
        // BALANS_CHECK_ON: public bool IsCompleted => ShouldPay > 0 && BankBalance >= ShouldPay;

        public string ShouldPayText => $"To'lanishi kerak: {ShouldPay:N0} so'm";
        public string BankBalanceText => $"To'landi: {BankBalance:N0} so'm";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void AddBankBalance(int amount)
        {
            BankBalance += amount;
        }

        public void ConfirmPayment()
        {
            if (!IsCompleted) return;
            // BALANS_CHECK_OFF - Balans ayirilmaydi, faqat ShouldPay sifirlanadi
            // BALANS_CHECK_ON: var newBalance = BankBalance - ShouldPay;
            // BALANS_CHECK_ON: BankBalance = newBalance;
            ShouldPay = 0;
        }

        /// <summary>
        /// Og'irlikni narxlar ro'yxatiga ko'ra hisoblab, to'lanishi kerak bo'lgan summani qaytaradi
        /// Narxlar ro'yxatida har bir entry.Kilogram - maksimal og'irlik, entry.Price - shu og'irlikgacha bo'lgan narx
        /// Og'irlikni narxlar ro'yxatiga ko'ra topib, mos narxni qaytaradi
        /// </summary>
        public int CalculatePriceByWeight(double weightKg, List<PriceEntry> prices)
        {
            if (prices == null || prices.Count == 0)
            {
                return 0;
            }

            // Narxlarni og'irlik bo'yicha tartiblash (kichikdan kattaga)
            var sortedPrices = prices.OrderBy(p => p.Kilogram).ToList();
            
            // Og'irlikni narxlar ro'yxatiga ko'ra topish
            // Og'irlikdan katta yoki teng bo'lgan birinchi narxni qaytarish
            foreach (var entry in sortedPrices)
            {
                if (weightKg <= entry.Kilogram)
                {
                    return (int)entry.Price;
                }
            }

            // Agar og'irlik barcha diapazonlardan oshib ketsa, oxirgi (eng katta) narxni ishlatish
            var lastEntry = sortedPrices[sortedPrices.Count - 1];
            return (int)lastEntry.Price;
        }
    }
}
