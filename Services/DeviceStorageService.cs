using System.Linq;
using System.Text.Json;
using TaroziAPP.Models;
using Microsoft.Maui.Storage;

namespace TaroziAPP.Services
{
    /// <summary>
    /// Service for storing and retrieving device information
    /// </summary>
    public sealed class DeviceStorageService
    {
        private const string DeviceKey = "saved_device";

        public async Task SaveAsync(Models.Device device)
        {
            object? paymentsData = null;
            if (device.Payments != null && device.Payments.Count > 0)
            {
                paymentsData = device.Payments.Select(p => new
                {
                    id = p.Id,
                    merchantId = p.MerchantId,
                    serviceId = p.ServiceId,
                    type = p.Type != null ? new
                    {
                        name = p.Type.Name,
                        photo = p.Type.Photo
                    } : null
                }).ToList();
            }
            else
            {
                paymentsData = new List<object>();
            }

            var json = JsonSerializer.Serialize(new
            {
                id = device.Id,
                name = device.Name,
                phoneNumber = device.PhoneNumber,
                password = device.Password,
                payments = paymentsData
            });

            Preferences.Set(DeviceKey, json);
        }

        public async Task<Models.Device?> GetAsync()
        {
            var json = Preferences.Get(DeviceKey, "");
            if (string.IsNullOrWhiteSpace(json)) 
            {
                return null;
            }

            var deviceData = JsonSerializer.Deserialize<JsonElement>(json);
            if (deviceData.ValueKind != JsonValueKind.Object) 
            {
                return null;
            }

            var device = new Models.Device
            {
                Id = deviceData.GetProperty("id").GetString() ?? "",
                Name = deviceData.GetProperty("name").GetString() ?? "",
                PhoneNumber = deviceData.GetProperty("phoneNumber").GetString() ?? "",
                Password = deviceData.GetProperty("password").GetString() ?? "",
                Payments = deviceData.TryGetProperty("payments", out var paymentsProp) && paymentsProp.ValueKind == JsonValueKind.Array
                    ? paymentsProp.EnumerateArray().Select(p => new Payment
                    {
                        Id = p.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                        MerchantId = p.TryGetProperty("merchantId", out var merchantIdProp) ? merchantIdProp.GetString() ?? "" : "",
                        ServiceId = p.TryGetProperty("serviceId", out var serviceIdProp) ? serviceIdProp.GetString() ?? "" : "",
                        Type = p.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.Object
                            ? new PaymentType
                            {
                                Name = typeProp.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                                Photo = typeProp.TryGetProperty("photo", out var photoProp) ? photoProp.GetString() ?? "" : ""
                            }
                            : null
                    }).ToList()
                    : new List<Payment>()
            };

            return device;
        }

        public async Task ClearAsync()
        {
            Preferences.Remove(DeviceKey);
        }
    }
}

