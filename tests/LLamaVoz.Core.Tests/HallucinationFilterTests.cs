using LLamaVoz.DesktopApp.Services;

namespace LLamaVoz.Core.Tests;

/// <summary>
/// FR-016 faithfulness guards: whisper's hallucination signatures must be filtered,
/// real dictation must pass through untouched.
/// </summary>
public class HallucinationFilterTests
{
    [Theory]
    [InlineData("[Música]")]
    [InlineData("(Aplausos)")]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("♪♪")]
    [InlineData("*suspiro*")]
    [InlineData("   ")]
    public void NonSpeechAnnotations_AreDetected(string text)
    {
        Assert.True(TranscriptionService.IsNonSpeechAnnotation(text));
    }

    [Theory]
    [InlineData("Hola, ¿cómo estás?")]
    [InlineData("El resultado es [importante] para todos")] // brackets inside text, not the whole segment
    [InlineData("2 + 2 = 4")]
    public void RealDictation_IsNotFlaggedAsAnnotation(string text)
    {
        Assert.False(TranscriptionService.IsNonSpeechAnnotation(text));
    }

    [Fact]
    public void StrayCjkChars_AreStripped_WhenMinority()
    {
        var result = TranscriptionService.StripStrayForeignChars("hola 好 mundo entero de verdad");
        Assert.DoesNotContain('好', result);
        Assert.Contains("hola", result);
        Assert.Contains("mundo", result);
    }

    [Fact]
    public void MostlyForeignText_IsLeftForMajorityCheck_NotStripped()
    {
        const string chinese = "你好世界你好世界";
        Assert.Equal(chinese, TranscriptionService.StripStrayForeignChars(chinese));
    }

    [Fact]
    public void Rms_OfDigitalSilence_IsZero()
    {
        Assert.Equal(0, TranscriptionService.Rms(new byte[32000]));
    }

    [Fact]
    public void Rms_OfLoudSignal_IsHigh()
    {
        // Full-scale square wave alternating ±16384 (half amplitude) → RMS 0.5
        var pcm = new byte[32000];
        for (var i = 0; i < pcm.Length; i += 2)
        {
            var sample = (short)(i % 4 == 0 ? 16384 : -16384);
            pcm[i] = (byte)(sample & 0xFF);
            pcm[i + 1] = (byte)(sample >> 8);
        }
        Assert.True(TranscriptionService.Rms(pcm) > 0.4);
    }

    [Theory]
    [InlineData("你好世界", true)]
    [InlineData("Привет мир как дела", true)]
    [InlineData("hola mundo", false)]
    [InlineData("hello world with ñ and é", false)]
    public void ForeignScriptMajority_IsDetected(string text, bool expected)
    {
        Assert.Equal(expected, TranscriptionService.IsMostlyForeignScript(text));
    }
}
