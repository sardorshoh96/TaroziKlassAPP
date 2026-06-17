using System;
using System.Linq;
using System.Text;

namespace TaroziAPP.Models.DeviceTypes
{
    /// <summary>
    /// XK3118T1 qurilma turi konfiguratsiyasi
    /// </summary>
    public class XK3118T1Device
    {
        public string Name => "XK3118T1";
        public string PortName => "/dev/ttyS0";
        public int BaudRate => 9600;
        public string Description => "XK3118T1 taroziklass qurilmasi";

        /// <summary>
        /// Og'irlik ma'lumotini qayta ishlab, og'irlikni qaytaradi (gramm)
        /// </summary>
        public int? ProcessWeightData(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 8)
            {
                return null;
            }

            // XK3118T1 protokol
            if (buffer[7] == 0x3D)
            {
                try
                {
                    // ASCII data olish
                    byte[] weightBytes =
                    {
                        buffer[0],
                        buffer[1],
                        buffer[2],
                        buffer[3],
                        buffer[4],
                        buffer[5],
                        buffer[6]
                      
                    };

                    string raw = Encoding.ASCII.GetString(weightBytes);


                    // Teskari aylantirish
                    string reversed = new string(raw.Reverse().ToArray());

                    // Oldidagi nolalarni olib tashlash
                    reversed = reversed.TrimStart('0');

                    if (string.IsNullOrEmpty(reversed))
                        reversed = "0";

                    // Integer parse
                    if (int.TryParse(reversed, out int weightGram))
                    {
                        return weightGram*1000;
                    }
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }
}
