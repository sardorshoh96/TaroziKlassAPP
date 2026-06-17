using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TaroziAPP.Models;
using Microsoft.Maui.Storage;

namespace TaroziAPP.Services
{
    /// <summary>
    /// Provider for managing transaction logs
    /// </summary>
    public sealed class LogsProvider
    {
        private readonly List<Logs> _logs;
        private const string LogsKey = "logs";

        public LogsProvider()
        {
            _logs = new List<Logs>();
            LoadLogsAsync();
        }

        public List<Logs> GetLogs()
        {
            lock (_logs)
            {
                return new List<Logs>(_logs);
            }
        }

        private async void LoadLogsAsync()
        {
            var storedJson = Preferences.Get(LogsKey, "");
            if (string.IsNullOrWhiteSpace(storedJson)) return;

            var logsList = JsonSerializer.Deserialize<List<JsonElement>>(storedJson);
            if (logsList == null) return;

            lock (_logs)
            {
                _logs.Clear();
                foreach (var logElement in logsList)
                {
                    var log = new Logs(
                        paymentId: logElement.TryGetProperty("paymentId", out var paymentIdProp) ? paymentIdProp.GetString() : null,
                        productId: logElement.TryGetProperty("productId", out var productIdProp) ? productIdProp.GetString() : null,
                        time: logElement.TryGetProperty("time", out var timeProp) ? timeProp.GetInt64() : null,
                        qty: logElement.TryGetProperty("qty", out var qtyProp) ? qtyProp.GetInt32() : null,
                        price: logElement.TryGetProperty("price", out var priceProp) ? priceProp.GetInt32() : null,
                        discount: logElement.TryGetProperty("discount", out var discountProp) ? discountProp.GetInt32() : null,
                        totalPrice: logElement.TryGetProperty("totalPrice", out var totalPriceProp) ? totalPriceProp.GetInt32() : null,
                        status: logElement.TryGetProperty("status", out var statusProp) ? statusProp.GetInt32() : null
                    );
                    _logs.Add(log);
                }
            }
        }

        private void SaveLogs()
        {
            List<Logs> logsCopy;
            lock (_logs)
            {
                logsCopy = new List<Logs>(_logs);
            }

            var logsJson = JsonSerializer.Serialize(logsCopy.Select(log => new
            {
                paymentId = log.PaymentId,
                productId = log.ProductId,
                time = log.Time,
                qty = log.Qty,
                price = log.Price,
                discount = log.Discount,
                totalPrice = log.TotalPrice,
                status = log.Status
            }).ToList());

            Preferences.Set(LogsKey, logsJson);
        }

        public void AddLog(Logs log)
        {
            lock (_logs)
            {
                _logs.Add(log);
            }
            SaveLogs();
        }

        public void ClearLogs()
        {
            lock (_logs)
            {
                _logs.Clear();
            }
            Preferences.Remove(LogsKey);
        }
    }
}

