using FlaUI.Core.AutomationElements;

namespace LLamaVoz.Insertion;

/// <summary>
/// Detects password/sensitive fields so the engine never inserts into or processes them (FR-030).
/// UIA exposure is not guaranteed for every app, so a negative result is not a guarantee —
/// this is one layer, complemented by literal/private modes.
/// </summary>
public static class SensitiveFieldDetector
{
    public static bool IsSensitive(AutomationElement? element)
    {
        if (element is null)
        {
            return false;
        }

        try
        {
            return element.Properties.IsPassword.ValueOrDefault;
        }
        catch
        {
            // If the provider throws while reading the property, err on the side of caution.
            return true;
        }
    }
}
