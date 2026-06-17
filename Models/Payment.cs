namespace TaroziAPP.Models;

public class Payment
{
    public string Id { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public bool Allow { get; set; }
    public PaymentType? Type { get; set; }
}

public class PaymentType
{
    public string Name { get; set; } = string.Empty;
    public string Photo { get; set; } = string.Empty;
}
