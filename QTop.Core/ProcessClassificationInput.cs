namespace QTop.Core;

public sealed record ProcessClassificationInput
{
    public bool HasVisibleMainWindow { get; init; }
    public bool IsService { get; init; }
    public bool IsSessionZero { get; init; }
    public bool IsWindowsOwned { get; init; }
    public bool IsKnownSystemProcess { get; init; }
    public bool DetailsDenied { get; init; }
}
