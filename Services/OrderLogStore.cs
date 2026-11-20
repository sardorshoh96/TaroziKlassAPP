using System.Text;
using System.Text.Json;
using TaroziAPP.Models;

namespace TaroziAPP.Services
{
    /// <summary>
    /// Serverga hali yuborilmagan sotuv loglari — diskdagi fayl (har qatorda bitta JSON yozuv).
    ///  • Har kupyura faylning OXIRIGA qo'shiladi (butun ro'yxat qayta yozilmaydi — tez va xavfsiz);
    ///  • Server qabul qilgach FAQAT yuborilgan yozuvlar o'chiriladi — yuborish paytida tushgan kupyura saqlanib qoladi;
    ///  • Fayl 50 MB dan oshsa eng eski yozuvlar o'chiriladi (45 MB gacha) — xotira to'lib qolmasin;
    ///  • Elektr o'chib oxirgi qator chala qolsa — faqat o'sha qator tashlanadi, qolganlari o'qiladi.
    /// MAUI ga bog'liq emas (kompyuterda sinash mumkin). Thread-safe.
    /// </summary>
    public sealed class OrderLogStore
    {
        public const long MaxBytes = 50L * 1024 * 1024;
        public const long TrimTargetBytes = 45L * 1024 * 1024;

        readonly string _path;
        readonly object _gate = new();
        readonly List<Logs> _items = new();
        readonly List<int> _sizes = new();                 // har yozuvning fayldagi bayt hajmi (qator + \n)
        readonly Dictionary<string, string> _strings = new(); // paymentId/productId bir xil — xotirani tejash
        long _bytes;

        public OrderLogStore(string directory, string fileName = "order_logs.jsonl")
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, fileName);
            Load();
        }

        public string FilePath => _path;
        public int Count { get { lock (_gate) return _items.Count; } }
        public long Bytes { get { lock (_gate) return _bytes; } }
        /// <summary>50 MB chegarasi sababli o'chirilgan eng eski yozuvlar soni (shu ishga tushirishda).</summary>
        public int Trimmed { get; private set; }
        /// <summary>Faylda o'qib bo'lmagan (buzilgan) qatorlar soni — ular tashlab ketildi.</summary>
        public int CorruptSkipped { get; private set; }

        public void Add(Logs log) => AddRange(new[] { log });

        public void AddRange(IEnumerable<Logs> logs)
        {
            lock (_gate)
            {
                var sb = new StringBuilder();
                foreach (var log in logs)
                {
                    Intern(log);
                    var line = Serialize(log);
                    sb.Append(line).Append('\n');
                    _items.Add(log);
                    int size = Encoding.UTF8.GetByteCount(line) + 1;
                    _sizes.Add(size);
                    _bytes += size;
                }
                if (sb.Length == 0) return;

                try
                {
                    using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
                    var data = Encoding.UTF8.GetBytes(sb.ToString());
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true);
                }
                catch (Exception ex)
                {
                    // Disk xatosi: yozuv xotirada qoladi va baribir yuboriladi; keyingi qayta yozishda faylga tushadi
                    System.Diagnostics.Debug.WriteLine($"[OrderLogStore] faylga yozilmadi: {ex.Message}");
                }

                if (_bytes > MaxBytes) TrimOldest();
            }
        }

        /// <summary>Eng eski yozuvlardan boshlab ko'pi bilan <paramref name="max"/> tasi (yuborish uchun).</summary>
        public List<Logs> Snapshot(int max = int.MaxValue)
        {
            lock (_gate) return _items.Take(max).ToList();
        }

        /// <summary>Server qabul qilgan yozuvlarni o'chiradi (aynan o'sha obyektlar — boshqalari saqlanadi).</summary>
        public void RemoveSent(IReadOnlyCollection<Logs> sent)
        {
            if (sent.Count == 0) return;
            lock (_gate)
            {
                var set = new HashSet<Logs>(sent, ReferenceEqualityComparer.Instance);
                bool changed = false;
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    if (!set.Contains(_items[i])) continue;
                    _bytes -= _sizes[i];
                    _items.RemoveAt(i);
                    _sizes.RemoveAt(i);
                    changed = true;
                }
                if (changed) Rewrite();
            }
        }

        /// <summary>1.0.5 va undan oldingi versiya formati: Preferences "logs" dagi JSON massiv.</summary>
        public static List<Logs> ParseLegacyJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<Logs>();
            return JsonSerializer.Deserialize<List<Logs>>(json) ?? new List<Logs>();
        }

        void Load()
        {
            // Qayta yozish paytida uzilib qolgan vaqtinchalik fayl — asosiy fayl butun, uni tashlaymiz
            try { if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp"); } catch { }
            if (!File.Exists(_path)) return;

            lock (_gate)
            {
                foreach (var line in File.ReadLines(_path, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    Logs? log = null;
                    try { log = JsonSerializer.Deserialize<Logs>(line); }
                    catch { }
                    if (log == null) { CorruptSkipped++; continue; }
                    Intern(log);
                    _items.Add(log);
                    int size = Encoding.UTF8.GetByteCount(line) + 1;
                    _sizes.Add(size);
                    _bytes += size;
                }
                if (CorruptSkipped > 0) Rewrite();          // faylni tozalaymiz
                if (_bytes > MaxBytes) TrimOldest();
            }
        }

        void TrimOldest()
        {
            int n = 0;
            long bytes = _bytes;
            while (n < _items.Count && bytes > TrimTargetBytes) { bytes -= _sizes[n]; n++; }
            if (n == 0) return;
            _items.RemoveRange(0, n);
            _sizes.RemoveRange(0, n);
            _bytes = bytes;
            Trimmed += n;
            System.Diagnostics.Debug.WriteLine($"[OrderLogStore] ⚠️ 50 MB chegarasi: eng eski {n} ta yozuv o'chirildi");
            Rewrite();
        }

        /// <summary>Faylni xotiradagi ro'yxat bilan almashtiradi (vaqtinchalik faylga yozib, so'ng atomik almashtirish).</summary>
        void Rewrite()
        {
            var tmp = _path + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    foreach (var log in _items) { w.Write(Serialize(log)); w.Write('\n'); }
                    w.Flush();
                    fs.Flush(true);
                }
                File.Move(tmp, _path, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderLogStore] qayta yozilmadi: {ex.Message}");
            }
        }

        void Intern(Logs log)
        {
            log.PaymentId = InternString(log.PaymentId);
            log.ProductId = InternString(log.ProductId);
        }

        string? InternString(string? s)
        {
            if (s == null) return null;
            if (_strings.TryGetValue(s, out var existing)) return existing;
            _strings[s] = s;
            return s;
        }

        static string Serialize(Logs log) => JsonSerializer.Serialize(log);
    }
}
