namespace TaroziAPP.Services.Ccnet;

/// <summary>GET BILL TABLE javobidagi bitta nominal yozuvi.</summary>
public readonly record struct BillDenomination(int TypeCode, long Amount, string Currency, bool Enabled)
{
    public bool IsValid => Amount > 0;
    public override string ToString() => $"#{TypeCode}: {Amount:N0} {Currency}";
}
