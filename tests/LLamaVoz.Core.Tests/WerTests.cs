using LLamaVoz.Evals;

namespace LLamaVoz.Core.Tests;

/// <summary>Self-test of the WER metric the eval suite relies on.</summary>
public class WerTests
{
    [Fact]
    public void IdenticalText_IsZero()
    {
        Assert.Equal(0, Wer.Compute("hola mundo feliz", "hola mundo feliz"));
    }

    [Fact]
    public void CompletelyDifferent_IsOne()
    {
        Assert.Equal(1, Wer.Compute("uno dos tres cuatro", "alfa beta gamma delta"));
    }

    [Fact]
    public void OneSubstitutionInFourWords_IsQuarter()
    {
        Assert.Equal(0.25, Wer.Compute("uno dos tres cuatro", "uno dos tres cinco"));
    }

    [Fact]
    public void PunctuationCaseAndAccents_AreIgnored()
    {
        Assert.Equal(0, Wer.Compute("Mañana, reunión a las TRES.", "mañana reunion a las tres"));
    }

    [Fact]
    public void EmptyHypothesis_IsTotalError()
    {
        Assert.Equal(1, Wer.Compute("algo que decir", ""));
    }

    [Fact]
    public void InsertionsCountAsErrors()
    {
        // 2 insertions over 4 reference words = 0.5
        Assert.Equal(0.5, Wer.Compute("uno dos tres cuatro", "uno dos extra tres cuatro final"));
    }
}
