using System.Text.Json.Serialization;

namespace TaroziAPP.Services.Api;

public sealed class JsonRpcRequest
{
    public JsonRpcRequest(string method, object? parameters = null, long? id = null)
    {
        Method = method;
        Params = parameters ?? new { };
        Id = id ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; } = "2.0";

    [JsonPropertyName("id")]
    public long Id { get; }

    [JsonPropertyName("method")]
    public string Method { get; }

    [JsonPropertyName("params")]
    public object Params { get; }
}

