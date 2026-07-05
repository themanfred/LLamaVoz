namespace LLamaVoz.Core.Insertion;

/// <summary>Insertion methods, ordered from most to least preferred (PRD §21).</summary>
public enum InsertionMethod
{
    None = 0,
    UiaValuePattern,
    UnicodeKeyboardInput,
    ClipboardPaste,
    ManualFallback,
}

public enum InsertionFailureReason
{
    None = 0,
    TargetWindowChanged,
    SensitiveFieldBlocked,
    EmptyText,
    AllMethodsFailed,
}

public sealed record InsertionOptions
{
    public bool AllowUia { get; init; } = true;
    public bool AllowKeyboardInput { get; init; } = true;
    public bool AllowClipboard { get; init; } = true;

    /// <summary>Wait after sending Ctrl+V before restoring the clipboard.</summary>
    public TimeSpan ClipboardPasteDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Skip the foreground-window check. Only for controlled tests, never in product flows (FR-028).</summary>
    public bool SkipTargetWindowVerification { get; init; }
}

public sealed record InsertionResult
{
    public required bool Success { get; init; }
    public required InsertionMethod MethodUsed { get; init; }
    public InsertionFailureReason FailureReason { get; init; } = InsertionFailureReason.None;
    public int CharactersInserted { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string? Detail { get; init; }

    public static InsertionResult Blocked(InsertionFailureReason reason, string detail) => new()
    {
        Success = false,
        MethodUsed = InsertionMethod.None,
        FailureReason = reason,
        Detail = detail,
    };
}
