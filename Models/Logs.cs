using System.Text.Json.Serialization;

namespace TaroziAPP.Models
{
    /// <summary>
    /// Logs model for transaction logging
    /// </summary>
    public class Logs
    {
        [JsonPropertyName("paymentId")]
        public string? PaymentId { get; set; }

        [JsonPropertyName("productId")]
        public string? ProductId { get; set; }

        [JsonPropertyName("time")]
        public long? Time { get; set; }

        [JsonPropertyName("qty")]
        public int? Qty { get; set; }

        [JsonPropertyName("price")]
        public int? Price { get; set; }

        [JsonPropertyName("discount")]
        public int? Discount { get; set; }

        [JsonPropertyName("totalPrice")]
        public int? TotalPrice { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }

        public Logs()
        {
        }

        public Logs(string? paymentId, string? productId, long? time, int? qty, int? price, int? discount, int? totalPrice, int? status)
        {
            PaymentId = paymentId;
            ProductId = productId;
            Time = time;
            Qty = qty;
            Price = price;
            Discount = discount;
            TotalPrice = totalPrice;
            Status = status;
        }
    }
}

