using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text.Json;

namespace LLamaVoz.Evals.Audio;

public sealed record AudioCase(string Id, string File, string Lang, string Voice, string Reference)
{
    public string AbsolutePath => Path.Combine(RepoPaths.Root, File.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>
/// Loads evals/cases.json and generates any missing WAVs with Windows TTS (16 kHz mono).
/// Idempotent: existing files are never regenerated. Synthetic audio — the report labels
/// WER figures accordingly.
/// </summary>
public static class TtsCaseGenerator
{
    public static IReadOnlyDictionary<string, AudioCase> LoadAndEnsure()
    {
        var json = JsonDocument.Parse(File.ReadAllText(RepoPaths.CasesPath));
        var cases = new Dictionary<string, AudioCase>();
        foreach (var element in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            var audioCase = new AudioCase(
                element.GetProperty("id").GetString()!,
                element.GetProperty("file").GetString()!,
                element.GetProperty("lang").GetString()!,
                element.GetProperty("voice").GetString()!,
                element.GetProperty("reference").GetString()!);
            cases[audioCase.Id] = audioCase;
        }

        Directory.CreateDirectory(RepoPaths.EvalAudioDir);
        var missing = cases.Values.Where(c => !File.Exists(c.AbsolutePath)).ToList();
        if (missing.Count > 0)
        {
            Console.WriteLine($"  Generando {missing.Count} WAV(s) de prueba con TTS de Windows...");
            var format = new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono);
            using var synth = new SpeechSynthesizer();
            foreach (var audioCase in missing)
            {
                try
                {
                    synth.SelectVoice(audioCase.Voice);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    ADVERTENCIA: voz '{audioCase.Voice}' no disponible ({ex.Message}); caso {audioCase.Id} sin audio.");
                    continue;
                }
                synth.SetOutputToWaveFile(audioCase.AbsolutePath, format);
                synth.Speak(audioCase.Reference);
                synth.SetOutputToNull();
                Console.WriteLine($"    {audioCase.Id} → {audioCase.File}");
            }
        }
        return cases;
    }
}
