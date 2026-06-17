using System;
using System.Text;

namespace TaroziAPP.Models.DeviceTypes
{
    /// <summary>
    /// Standart qurilma turi konfiguratsiyasi
    /// </summary>
    public class StandartDevice
    {
        public string Name => "Standart";
        public string PortName => "/dev/ttyS7"; // RS485 uchun default port
        public int BaudRate => 9600;
        public string Description => "Standart taroziklass qurilmasi";
        
        /// <summary>
        /// Og'irlik ma'lumotini qayta ishlab, og'irlikni qaytaradi (gramm)
        /// </summary>
        public int? ProcessWeightData(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 12)
            {
                return null;
            }

            // Standart protokol: buffer[0] == 0x02 && buffer[1] == 0x2B
            if (buffer[0] == 0x02 && buffer[1] == 0x2B)
            {
                // Extract weight data from buffer[2] to buffer[7] as ASCII string
                byte[] weightBytes = { buffer[2], buffer[3], buffer[4], buffer[5], buffer[6], buffer[7] };
                string weightString = Encoding.ASCII.GetString(weightBytes).Trim();

                // Parse weight string to double (kg)
                if (double.TryParse(weightString, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double weightKg))
                {
                    // Convert kg to grams
                    return (int)(weightKg * 1000);
                }
            }
            
            return null;
        }
    }
}

