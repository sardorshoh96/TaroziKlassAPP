using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TaroziAPP.Services.Api;

namespace TaroziAPP.Services;

public sealed class JsonRpcClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private string? _savedLogin;
    private string? _savedPassword;

    public JsonRpcClient(string endpoint, HttpClient? httpClient = null)
    {
        _endpoint = endpoint;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30); // Set timeout to 30 seconds
    }

    /// <summary>
    /// Sets credentials to be used automatically for all requests
    /// </summary>
    public void SetCredentials(string login, string password)
    {
        _savedLogin = login;
        _savedPassword = password;
    }

    /// <summary>
    /// Clears saved credentials
    /// </summary>
    public void ClearCredentials()
    {
        _savedLogin = null;
        _savedPassword = null;
    }

    public async Task<ApiResult<T>> PostAsync<T>(JsonRpcRequest request, string? login = null, string? password = null, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        var payload = JsonSerializer.Serialize(request, SerializerOptions);
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        // Use provided credentials or saved credentials
        string authLogin = login ?? _savedLogin ?? "";
        string authPassword = password ?? _savedPassword ?? "";

        if (!string.IsNullOrEmpty(authLogin) && !string.IsNullOrEmpty(authPassword))
        {
            var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{authLogin}:{authPassword}"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
        }

        Console.WriteLine($"[JsonRpcClient] Sending request to {_endpoint}");
        Console.WriteLine($"[JsonRpcClient] Request payload: {payload}");

        HttpResponseMessage? response = null;
        string responseBody = "";

        try
        {
            response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"[JsonRpcClient] Response status: {response.StatusCode}");
            Console.WriteLine($"[JsonRpcClient] Response body: {responseBody}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[JsonRpcClient] ⚠️ Connection failure: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[JsonRpcClient] ⚠️ Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            // Silent fail - return failure result instead of throwing
            return ApiResult<T>.Failure($"Connection failure: {ex.Message}");
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            Console.WriteLine($"[JsonRpcClient] ⚠️ Socket error: {ex.Message}");
            // Silent fail - return failure result instead of throwing
            return ApiResult<T>.Failure($"Socket error: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            Console.WriteLine($"[JsonRpcClient] ⚠️ Request timeout");
            // Silent fail - return failure result instead of throwing
            return ApiResult<T>.Failure("Request timeout");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JsonRpcClient] ⚠️ Request error: {ex.Message}");
            // Silent fail - return failure result instead of throwing
            return ApiResult<T>.Failure($"Request error: {ex.Message}");
        }

        if (response == null)
        {
            return ApiResult<T>.Failure("No response received");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return ApiResult<T>.Failure($"HTTP {response.StatusCode}: {responseBody}");
            }

            var rpcResponse = JsonSerializer.Deserialize<JsonRpcResponse<T>>(responseBody, SerializerOptions);
            if (rpcResponse?.Error != null)
            {
                Console.WriteLine($"[JsonRpcClient] RPC Error: {rpcResponse.Error.Code} - {rpcResponse.Error.Message}");
                return ApiResult<T>.Failure($"{rpcResponse.Error.Code}: {rpcResponse.Error.Message}");
            }

            if (rpcResponse == null)
            {
                Console.WriteLine("[JsonRpcClient] RPC Response is null");
                return ApiResult<T>.Failure("Empty result");
            }

            // Check if result is null (for reference types) or default (for value types)
            var resultValue = rpcResponse.Result;
            Console.WriteLine($"[JsonRpcClient] RPC Result type: {typeof(T).Name}, Result is null: {resultValue == null}");

            if (resultValue == null)
            {
                Console.WriteLine("[JsonRpcClient] RPC Result is null");
                return ApiResult<T>.Failure("Empty result");
            }

            // For value types, check if equals default value
            if (typeof(T).IsValueType && resultValue.Equals(Activator.CreateInstance(typeof(T))))
            {
                Console.WriteLine("[JsonRpcClient] RPC Result equals default value");
                return ApiResult<T>.Failure("Empty result");
            }

            Console.WriteLine($"[JsonRpcClient] RPC Result deserialized successfully. Type: {resultValue.GetType().Name}");
            return ApiResult<T>.Success(resultValue);
        }
    }
}


