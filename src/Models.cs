namespace Lofrano.BoundedObservedSet;

public readonly record struct Dot(string Replica, long Counter);

public enum ResultCode
{
    Success,
    InvalidElement,
    HistoryLimit,
    ElementLimit,
    CounterOverflow,
    IncompatibleMembership,
    DotConflict,
    InvalidSnapshot,
    SnapshotTooLarge
}

public readonly record struct OperationResult(ResultCode Code, Dot? Dot = null)
{
    public bool IsSuccess => Code == ResultCode.Success;
}

public sealed record SetLimits(int HistoryDots = 4096, int DistinctElements = 1024);
