using System.Text.Json.Serialization;

namespace TaroziAPP.Services.Api;

public sealed class TransactionDto
{
    [JsonPropertyName("create_time")]
    public long CreateTime { get; set; }

    [JsonPropertyName("transaction")]
    public string? Transaction { get; set; }

    [JsonPropertyName("state")]
    public int State { get; set; }
}

public sealed class CheckTransactionDto
{
    [JsonPropertyName("create_time")]
    public long CreateTime { get; set; }

    [JsonPropertyName("perform_time")]
    public long PerformTime { get; set; }

    [JsonPropertyName("cancel_time")]
    public long CancelTime { get; set; }

    [JsonPropertyName("transaction")]
    public string? Transaction { get; set; }

    [JsonPropertyName("state")]
    public int State { get; set; }

    [JsonPropertyName("reason")]
    public object? Reason { get; set; }
}

