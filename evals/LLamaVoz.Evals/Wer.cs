using System.Globalization;
using System.Text;

namespace LLamaVoz.Evals;

/// <summary>
/// Word Error Rate: word-level Levenshtein distance / reference length.
/// Normalization is deliberately lenient (lowercase, punctuation and diacritics removed)
/// so WER measures word identity, not styling — the report labels it as such.
/// </summary>
public static class Wer
{
    public static double Compute(string reference, string hypothesis)
    {
        var refWords = Normalize(reference);
        var hypWords = Normalize(hypothesis);
        if (refWords.Length == 0)
        {
            return hypWords.Length == 0 ? 0 : 1;
        }
        return (double)Levenshtein(refWords, hypWords) / refWords.Length;
    }

    public static string[] Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue; // drop diacritics: "reunión" ≡ "reunion"
            }
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch))
            {
                sb.Append(' ');
            }
            // punctuation dropped entirely
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static int Levenshtein(string[] a, string[] b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
