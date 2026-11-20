using System.Collections.Generic;
using TaroziAPP.Models;
using Microsoft.Maui.Storage;

namespace TaroziAPP.Services
{
    /// <summary>
    /// Sotuv loglari (hisobot uchun). 1.0.6 dan boshlab diskdagi faylda saqlanadi (<see cref="OrderLogStore"/>):
    /// yuborilganlari o'chiriladi, yuborish paytida tushgani yo'qolmaydi, 50 MB dan oshmaydi.
    /// 1.0.5 dan qolgan (Preferences "logs") yuborilmagan loglar birinchi ishga tushishda faylga ko'chiriladi.
    /// </summary>
    public sealed class LogsProvider
    {
        private const string LegacyLogsKey = "logs";
        private readonly OrderLogStore _store;

        public LogsProvider()
        {
            _store = new OrderLogStore(FileSystem.AppDataDirectory);
            MigrateLegacyLogs();
            System.Diagnostics.Debug.WriteLine($"[LogsProvider] yuborilmagan loglar: {_store.Count} ta ({_store.Bytes / 1024} KB)");
        }

        /// <summary>Yuborilmagan loglar soni.</summary>
        public int Count => _store.Count;

        /// <summary>Eng eskisidan boshlab ko'pi bilan <paramref name="max"/> ta log (yuborish uchun).</summary>
        public List<Logs> GetLogs(int max = int.MaxValue) => _store.Snapshot(max);

        public void AddLog(Logs log) => _store.Add(log);

        /// <summary>Server qabul qilgan loglarni o'chiradi (faqat shularni).</summary>
        public void RemoveSent(IReadOnlyCollection<Logs> sent) => _store.RemoveSent(sent);

        private void MigrateLegacyLogs()
        {
            try
            {
                var json = Preferences.Get(LegacyLogsKey, "");
                if (string.IsNullOrWhiteSpace(json)) return;

                var old = OrderLogStore.ParseLegacyJson(json);
                _store.AddRange(old);
                // Faylga yozilgandan KEYIN o'chiramiz (orada uzilsa — keyingi safar qayta ko'chiriladi,
                // server bir xil sotuvni ikkinchi marta yozmaydi)
                Preferences.Remove(LegacyLogsKey);
                System.Diagnostics.Debug.WriteLine($"[LogsProvider] 1.0.5 dan {old.Count} ta yuborilmagan log faylga ko'chirildi");
            }
            catch (System.Exception ex)
            {
                // O'qib bo'lmadi — eski loglarni o'chirmaymiz (keyingi versiyada ham urinish mumkin)
                System.Diagnostics.Debug.WriteLine($"[LogsProvider] eski loglarni ko'chirib bo'lmadi: {ex.Message}");
            }
        }
    }
}
