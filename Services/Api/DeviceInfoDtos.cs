using System.Text.Json.Serialization;

namespace TaroziAPP.Services.Api;

public sealed class DeviceInfoDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("imei")]
    public string? Imei { get; set; }

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("servicePhoneNumber")]
    public string? ServicePhoneNumber { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("settings")]
    public string? Settings { get; set; }

    [JsonPropertyName("waitTime")]
    public int? WaitTime { get; set; }

    [JsonPropertyName("noEventTime")]
    public int? NoEventTime { get; set; }

    [JsonPropertyName("board")]
    public string? Board { get; set; }

    [JsonPropertyName("type")]
    public DeviceTypeDto? Type { get; set; }

    [JsonPropertyName("payments")]
    public List<PaymentDto> Payments { get; set; } = new();

    [JsonPropertyName("categories")]
    public List<CategoryDto> Categories { get; set; } = new();
}

public sealed class DeviceTypeDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class PaymentDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("merchantId")]
    public string? MerchantId { get; set; }

    [JsonPropertyName("serviceId")]
    public string? ServiceId { get; set; }

    [JsonPropertyName("allow")]
    public bool Allow { get; set; }

    [JsonPropertyName("type")]
    public PaymentTypeDto? Type { get; set; }
}

public sealed class PaymentTypeDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("photo")]
    public string? Photo { get; set; }
}

public sealed class CategoryDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("products")]
    public List<ProductDto> Products { get; set; } = new();
}

public sealed class ProductDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("address")]
    public int? Address { get; set; }

    [JsonPropertyName("price")]
    public int? Price { get; set; }

    [JsonPropertyName("enable")]
    public bool? Enable { get; set; }

    [JsonPropertyName("qty")]
    public int? Qty { get; set; }

    [JsonPropertyName("discount")]
    public int? Discount { get; set; }

    [JsonPropertyName("position")]
    public int? Position { get; set; }

    [JsonPropertyName("photo")]
    public ProductPhotoDto? Photo { get; set; }
}

public sealed class ProductPhotoDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("photo")]
    public string? Photo { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

