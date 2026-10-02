using System;
using System.Threading.Tasks;

namespace TaroziAPP.Services
{
    public interface IPrinterService : IDisposable
    {
        int Connect();
        // weightKg = brutto (o'lchangan) og'irlik; tara > 0 bo'lsa chekda Brutto/Tara/Netto ko'rsatiladi
        Task<bool> PrintReceiptAsync(double weightKg, DateTime dateTime, string address, string carNumber = "", double tara = 0);
    }
}
