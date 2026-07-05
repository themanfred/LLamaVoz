using LLamaVoz.Insertion.Native;

namespace LLamaVoz.Insertion.Tiers;

/// <summary>
/// Tier 2 — simulated Unicode keyboard input via SendInput (PRD §21.2).
/// Types at the caret, so it is non-destructive by construction. Newlines are sent as
/// Enter presses; callers should be aware Enter can submit in chat-style apps (documented risk).
/// </summary>
internal static class KeyboardInserter
{
    private const int ChunkSize = 32;

    public static TierOutcome TryInsert(string text)
    {
        try
        {
            var normalized = text.Replace("\r\n", "\n");
            var inputs = new List<NativeMethods.INPUT>(normalized.Length * 2);

            foreach (var ch in normalized)
            {
                if (ch == '\n')
                {
                    inputs.Add(NativeMethods.KeyDown(NativeMethods.VK_RETURN));
                    inputs.Add(NativeMethods.KeyUp(NativeMethods.VK_RETURN));
                }
                else
                {
                    inputs.Add(NativeMethods.UnicodeDown(ch));
                    inputs.Add(NativeMethods.UnicodeUp(ch));
                }
            }

            for (var offset = 0; offset < inputs.Count; offset += ChunkSize)
            {
                var chunk = inputs.Skip(offset).Take(ChunkSize).ToArray();
                var sent = NativeMethods.SendInput((uint)chunk.Length, chunk, NativeMethods.INPUT.Size);
                if (sent != chunk.Length)
                {
                    return TierOutcome.Failed(
                        $"SendInput sent {sent}/{chunk.Length} events (input may be blocked by an elevated window).");
                }

                // Give the target app's message queue room to breathe between chunks.
                Thread.Sleep(5);
            }

            return TierOutcome.Inserted($"Sent {normalized.Length} characters as Unicode keyboard input.");
        }
        catch (Exception ex)
        {
            return TierOutcome.Failed($"Keyboard insertion threw: {ex.Message}");
        }
    }
}
