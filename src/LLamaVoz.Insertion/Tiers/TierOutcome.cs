namespace LLamaVoz.Insertion.Tiers;

internal enum TierStatus
{
    /// <summary>The tier inserted the text.</summary>
    Inserted,

    /// <summary>The tier cannot safely handle this control; try the next tier.</summary>
    NotApplicable,

    /// <summary>The tier tried and failed; try the next tier.</summary>
    Failed,
}

internal sealed record TierOutcome(TierStatus Status, string? Detail = null, string? Warning = null)
{
    public static TierOutcome Inserted(string? detail = null) => new(TierStatus.Inserted, detail);
    public static TierOutcome NotApplicable(string detail) => new(TierStatus.NotApplicable, detail);
    public static TierOutcome Failed(string detail, string? warning = null) => new(TierStatus.Failed, detail, warning);
}
