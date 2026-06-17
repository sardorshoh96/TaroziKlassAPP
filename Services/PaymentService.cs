using TaroziAPP.Models;
using TaroziAPP.Services.Api;

namespace TaroziAPP.Services;

public sealed class PaymentService
{
    private readonly JsonRpcClient _client;

    public PaymentService(JsonRpcClient? client = null)
    {
        _client = client ?? new JsonRpcClient("https://device.radiomer.uz/api/device/v1");
    }

    /// <summary>
    /// Sets credentials to be used automatically for all requests
    /// </summary>
    public void SetCredentials(string login, string password)
    {
        _client.SetCredentials(login, password);
    }

    /// <summary>
    /// Clears saved credentials
    /// </summary>
    public void ClearCredentials()
    {
        _client.ClearCredentials();
    }

    public Task<ApiResult<TransactionDto>> CreateTransactionAsync(UserCredentials? credentials = null, string? paymentId = null, int amount = 0, CancellationToken cancellationToken = default)
    {
        var request = new JsonRpcRequest("CreateTransaction", new
        {
            paymentId,
            id = amount.ToString(),
            time = 1,
            amount = amount * 100
        });

        return _client.PostAsync<TransactionDto>(request, credentials?.Login, credentials?.Password, cancellationToken);
    }

    public Task<ApiResult<CheckTransactionDto>> CheckTransactionAsync(UserCredentials? credentials = null, string? paymentId = null, CancellationToken cancellationToken = default)
    {
        var request = new JsonRpcRequest("CheckTransaction", new
        {
            paymentId,
            id = ""
        });

        return _client.PostAsync<CheckTransactionDto>(request, credentials?.Login, credentials?.Password, cancellationToken);
    }
}

