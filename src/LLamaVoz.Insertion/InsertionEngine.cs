using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using LLamaVoz.Core.Insertion;
using LLamaVoz.Insertion.Tiers;

namespace LLamaVoz.Insertion;

/// <summary>
/// Tiered text insertion engine (PRD §21): UIA → Unicode keyboard input → clipboard paste →
/// manual fallback (text left in the clipboard). Enforces the two transversal rules before
/// touching anything: target-window verification (FR-028) and sensitive-field blocking (FR-030).
/// </summary>
public sealed class InsertionEngine : IDisposable
{
    private readonly UIA3Automation _automation = new();

    public InsertionResult Insert(string text, TargetWindowGuard target, InsertionOptions? options = null)
    {
        options ??= new InsertionOptions();

        if (string.IsNullOrEmpty(text))
        {
            return InsertionResult.Blocked(InsertionFailureReason.EmptyText, "Nothing to insert.");
        }

        // Rule 1 (FR-028): never insert if the user switched windows while we were processing.
        if (!options.SkipTargetWindowVerification &&
            !target.IsStillForeground(out var currentTitle))
        {
            return InsertionResult.Blocked(
                InsertionFailureReason.TargetWindowChanged,
                $"Foreground window changed from '{target.WindowTitle}' to '{currentTitle}'. Not inserting.");
        }

        // Rule 2 (FR-030): never insert into password/sensitive fields.
        var focused = TryGetFocusedElement();
        if (SensitiveFieldDetector.IsSensitive(focused))
        {
            return InsertionResult.Blocked(
                InsertionFailureReason.SensitiveFieldBlocked,
                "Focused control is a password/sensitive field.");
        }

        var warnings = new List<string>();

        if (options.AllowUia)
        {
            var outcome = UiaInserter.TryInsert(focused, text);
            if (Report(outcome, InsertionMethod.UiaValuePattern, text, warnings) is { } result)
            {
                return result;
            }
        }

        if (options.AllowKeyboardInput)
        {
            var outcome = KeyboardInserter.TryInsert(text);
            if (Report(outcome, InsertionMethod.UnicodeKeyboardInput, text, warnings) is { } result)
            {
                return result;
            }
        }

        if (options.AllowClipboard)
        {
            var outcome = ClipboardInserter.TryInsert(text, options.ClipboardPasteDelay);
            if (Report(outcome, InsertionMethod.ClipboardPaste, text, warnings) is { } result)
            {
                return result;
            }
        }

        // Tier 4 (PRD §21.4): leave the text in the clipboard and tell the user to paste manually.
        return ManualFallback(text, warnings);
    }

    private AutomationElement? TryGetFocusedElement()
    {
        try
        {
            return _automation.FocusedElement();
        }
        catch
        {
            return null;
        }
    }

    private static InsertionResult? Report(
        TierOutcome outcome, InsertionMethod method, string text, List<string> warnings)
    {
        if (outcome.Warning is not null)
        {
            warnings.Add(outcome.Warning);
        }

        if (outcome.Status == TierStatus.Inserted)
        {
            return new InsertionResult
            {
                Success = true,
                MethodUsed = method,
                CharactersInserted = text.Length,
                Warnings = warnings.ToArray(),
                Detail = outcome.Detail,
            };
        }

        warnings.Add($"{method}: {outcome.Detail}");
        return null;
    }

    private static InsertionResult ManualFallback(string text, List<string> warnings)
    {
        try
        {
            StaRunner.Run(() =>
                System.Windows.Forms.Clipboard.SetDataObject(text, copy: true, retryTimes: 5, retryDelay: 50));

            return new InsertionResult
            {
                Success = false,
                MethodUsed = InsertionMethod.ManualFallback,
                FailureReason = InsertionFailureReason.AllMethodsFailed,
                Warnings = warnings.ToArray(),
                Detail = "All automatic methods failed. Text left in the clipboard — paste with Ctrl+V.",
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Manual fallback failed to set clipboard: {ex.Message}");
            return new InsertionResult
            {
                Success = false,
                MethodUsed = InsertionMethod.None,
                FailureReason = InsertionFailureReason.AllMethodsFailed,
                Warnings = warnings.ToArray(),
                Detail = "All insertion methods failed, including the clipboard fallback.",
            };
        }
    }

    public void Dispose() => _automation.Dispose();
}
