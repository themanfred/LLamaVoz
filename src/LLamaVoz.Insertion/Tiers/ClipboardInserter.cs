using LLamaVoz.Insertion.Native;

namespace LLamaVoz.Insertion.Tiers;

/// <summary>
/// Tier 3 — non-destructive clipboard paste (PRD §21.3, FR-027).
/// Backs up current clipboard text, places the new text, sends Ctrl+V, then restores the
/// previous clipboard content. Non-text clipboard content cannot be fully backed up yet;
/// in that case we proceed but report a warning (documented FR-027 limitation for the POC).
/// </summary>
internal static class ClipboardInserter
{
    public static TierOutcome TryInsert(string text, TimeSpan pasteDelay)
    {
        string? previousText = null;
        var hadNonTextContent = false;
        var hadAnyContent = false;

        try
        {
            StaRunner.Run(() =>
            {
                var data = System.Windows.Forms.Clipboard.GetDataObject();
                if (data is not null && data.GetFormats().Length > 0)
                {
                    hadAnyContent = true;
                    if (data.GetDataPresent(System.Windows.Forms.DataFormats.UnicodeText))
                    {
                        previousText = data.GetData(System.Windows.Forms.DataFormats.UnicodeText) as string;
                    }
                    else
                    {
                        hadNonTextContent = true;
                    }
                }

                System.Windows.Forms.Clipboard.SetDataObject(text, copy: true, retryTimes: 5, retryDelay: 50);
            });

            var ctrlV = new[]
            {
                NativeMethods.KeyDown(NativeMethods.VK_CONTROL),
                NativeMethods.KeyDown(NativeMethods.VK_V),
                NativeMethods.KeyUp(NativeMethods.VK_V),
                NativeMethods.KeyUp(NativeMethods.VK_CONTROL),
            };
            var sent = NativeMethods.SendInput((uint)ctrlV.Length, ctrlV, NativeMethods.INPUT.Size);
            if (sent != ctrlV.Length)
            {
                return TierOutcome.Failed($"SendInput for Ctrl+V sent {sent}/4 events.");
            }

            // Let the target app read the clipboard before we restore it.
            Thread.Sleep(pasteDelay);

            string? warning = null;
            StaRunner.Run(() =>
            {
                if (previousText is not null)
                {
                    System.Windows.Forms.Clipboard.SetDataObject(previousText, copy: true, retryTimes: 5, retryDelay: 50);
                }
                else if (!hadAnyContent)
                {
                    System.Windows.Forms.Clipboard.Clear();
                }
            });

            if (hadNonTextContent)
            {
                warning = "Previous clipboard content was not plain text and could not be restored (FR-027 limitation).";
            }

            return new TierOutcome(TierStatus.Inserted, "Clipboard paste with backup/restore.", warning);
        }
        catch (Exception ex)
        {
            return TierOutcome.Failed($"Clipboard insertion threw: {ex.Message}");
        }
    }
}
