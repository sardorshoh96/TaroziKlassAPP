using System;
using System.Text;

namespace TaroziAPP.Models.DeviceTypes
{
    /// <summary>
    /// A12E qurilma turi konfiguratsiyasi
    /// </summary>
    public class A12EDevice
    {
        public string Name => "A12E";
        public string PortName => "/dev/ttyS0"; // RS485 uchun default port
        public int BaudRate => 9600;
        public string Description => "A12E taroziklass qurilmasi";
        
        /// <summary>
        /// Og'irlik ma'lumotini qayta ishlab, og'irlikni qaytaradi (gramm)
        /// </summary>
        public int? ProcessWeightData(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 12)
            {
                return null;
            }

            // A12E protokol: buffer[0] == 0x02 && buffer[1] == 0x2B
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

