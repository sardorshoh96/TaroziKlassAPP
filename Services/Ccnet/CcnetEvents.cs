namespace TaroziAPP.Services.Ccnet;

public enum CcnetState
{
    Disconnected,
    PoweredUp,
    Initialize,
    Idle,
    Accepting,
    Stacking,
    Returning,
    Disabled,
    Holding,
    Busy,
    Rejecting,
    Escrow,
    Jammed,
    CassetteFull,
    CassetteRemoved,
    Failure
}

public sealed class BillStackedEventArgs(BillDenomination bill) : EventArgs
{
    public BillDenomination Bill { get; } = bill;
}

public sealed class CcnetStateChangedEventArgs(CcnetState state, string detail) : EventArgs
{
    public CcnetState State { get; } = state;
    public string Detail { get; } = detail;
}
