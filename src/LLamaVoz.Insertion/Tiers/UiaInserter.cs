using FlaUI.Core.AutomationElements;

namespace LLamaVoz.Insertion.Tiers;

/// <summary>
/// Tier 1 — UI Automation ValuePattern (PRD §21.1).
/// Only inserts when it is provably non-destructive: the pattern must be writable and the
/// control must currently be empty (ValuePattern.SetValue replaces the whole value, so using
/// it on a non-empty control would destroy content — NFR-21 forbids that). Non-empty
/// controls fall through to the keyboard tier, which types at the caret.
/// </summary>
internal static class UiaInserter
{
    public static TierOutcome TryInsert(AutomationElement? focused, string text)
    {
        if (focused is null)
        {
            return TierOutcome.NotApplicable("No focused UIA element.");
        }

        try
        {
            var valuePattern = focused.Patterns.Value.PatternOrDefault;
            if (valuePattern is null)
            {
                return TierOutcome.NotApplicable("Focused element does not support ValuePattern.");
            }

            if (valuePattern.IsReadOnly.ValueOrDefault)
            {
                return TierOutcome.NotApplicable("ValuePattern is read-only.");
            }

            var currentValue = valuePattern.Value.ValueOrDefault ?? string.Empty;
            if (currentValue.Length > 0)
            {
                return TierOutcome.NotApplicable(
                    "Control has existing content; ValuePattern.SetValue would replace it. Falling through.");
            }

            valuePattern.SetValue(text);

            // Read back to confirm the control actually accepted the value.
            var newValue = valuePattern.Value.ValueOrDefault ?? string.Empty;
            return newValue == text
                ? TierOutcome.Inserted("ValuePattern.SetValue on empty control.")
                : TierOutcome.Failed($"SetValue did not stick (read back {newValue.Length} chars).");
        }
        catch (Exception ex)
        {
            return TierOutcome.Failed($"UIA insertion threw: {ex.Message}");
        }
    }
}
