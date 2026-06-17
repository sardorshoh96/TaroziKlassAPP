using System;
using System.Text;

namespace TaroziAPP.Models.DeviceTypes
{
    /// <summary>
    /// TITAN H12 qurilma turi konfiguratsiyasi
    /// </summary>
    public class TITANH12Device
    {
        public string Name => "TITAN H12";
        public string PortName => "/dev/ttyS0"; // RS485 uchun default port
        public int BaudRate => 9600;
        public string Description => "TITAN H12 taroziklass qurilmasi";

        /// <summary>
        /// Og'irlik ma'lumotini qayta ishlab, og'irlikni qaytaradi (gramm)
        /// </summary>
        public int? ProcessWeightData(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 3)
                return null;

            try
            {
                byte[] dataToProcess;

                if (buffer.Length > 3)
                {
                    dataToProcess = new byte[3];
                    Array.Copy(buffer, buffer.Length - 3, dataToProcess, 0, 3);
                }
                else
                {
                    dataToProcess = buffer;
                }

                int value = (dataToProcess[0] << 16)
                          | (dataToProcess[1] << 8)
                          | dataToProcess[2];

                return value*1000;
            }
            catch
            {
                return null;
            }
        }
    }
}
