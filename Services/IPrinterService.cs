using System;
using System.Threading.Tasks;

namespace TaroziAPP.Services
{
    public interface IPrinterService : IDisposable
    {
        int Connect();
        Task<bool> PrintReceiptAsync(double weightKg, DateTime dateTime, string address, string carNumber = "");
    }
}
