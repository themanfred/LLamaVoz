using System.IO;
using System.Text;
using Whisper.net;

namespace LLamaVoz.DesktopApp.Services;

/// <summary>Model tier for speculative transcription: Draft = fast tiny model,
/// Verify = accurate base model. (DSpark-style draft→verify, applied to ASR.)</summary>
public enum ModelTier
{
    Draft,
    Verify,
}

public sealed record TranscriptionResult(string Text, string Language, float Confidence);

/// <summary>
/// Wraps the local Whisper models. Prototype note: runs in-process; the plan's separate
/// inference service (FR-038) comes later. Audio only ever exists in memory.
/// Two tiers are supported: tiny as a cheap drafter and base as the verifier; callers
/// (StreamingTranscriptionSession) route segments between them by confidence.
/// </summary>
public sealed class TranscriptionService : IDisposable
{
    // Below this average token probability a segment is flagged instead of trusted
    // (whisper invents plausible text on unclear audio; marking beats guessing).
    private const float LowConfidence = 0.30f;

    // Whole-clip RMS below this means there is no speech at all — whisper hallucinates
    // on silence ("mmm", "[Música]"…), so we return empty without invoking it. Kept well
    // under the streaming VAD's per-chunk speech threshold (0.015) so quiet-but-real
    // speech is never dropped; a silent room's mic noise floor sits around 0.001–0.005.
    private const double SilenceRmsThreshold = 0.006;

    private sealed class TierState
    {
        public string Path = "";
        public WhisperFactory? Factory;
        public WhisperProcessor? Processor;
        public string ProcessorLanguage = "auto";
        public int ProcessorAudioCtx = FullAudioContext;
    }

    // whisper pads every input to a 30 s window (1500 encoder frames), so a 3 s segment
    // costs almost as much as a 30 s one. Capping audio_ctx to the real audio length
    // makes encoder cost proportional to duration — the key to short tail latency.
    private const int FullAudioContext = 1500;

    private static int AudioContextFor(byte[] pcm)
    {
        var seconds = pcm.Length / 32000.0;
        var frames = (int)Math.Ceiling(seconds / 30.0 * FullAudioContext) + 128; // headroom
        frames = (frames + 127) / 128 * 128; // bucket to limit processor rebuilds
        return Math.Min(FullAudioContext, frames);
    }

    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1); // whisper inference is single-flight (CPU-bound)
    private readonly Dictionary<ModelTier, TierState> _tiers = new()
    {
        [ModelTier.Draft] = new TierState(),
        [ModelTier.Verify] = new TierState(),
    };

    /// <summary>Verifier (quality) model path — kept as the "main" model for display.</summary>
    public string ModelPath => _tiers[ModelTier.Verify].Path;

    public string DraftModelPath => _tiers[ModelTier.Draft].Path;

    /// <summary>True when draft and verify resolve to distinct models (both tiers useful).</summary>
    public bool HasDistinctTiers => DraftModelPath != ModelPath;

    public TranscriptionService()
    {
        var verify = ResolveModelPath("ggml-base.bin", "ggml-tiny.bin");
        var draft = ResolveModelPath("ggml-tiny.bin", "ggml-base.bin");
        _tiers[ModelTier.Verify].Path = verify;
        _tiers[ModelTier.Draft].Path = draft;
    }

    private static string ResolveModelPath(params string[] preference)
    {
        // LLAMAVOZ_MODEL overrides both tiers (single-model mode).
        var overridePath = Environment.GetEnvironmentVariable("LLAMAVOZ_MODEL");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        // Walk up from bin/ to find the repo's models/ folder.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent!)
        {
            var models = Path.Combine(dir.FullName, "models");
            if (!Directory.Exists(models))
            {
                continue;
            }
            foreach (var candidate in preference)
            {
                var path = Path.Combine(models, candidate);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        throw new FileNotFoundException(
            "No se encontró ningún modelo Whisper en models/ (ggml-base.bin o ggml-tiny.bin).");
    }

    /// <summary>Loads a tier's model ahead of the first dictation (~170 ms tiny, ~300 ms base).</summary>
    public void Preload(ModelTier tier = ModelTier.Verify)
    {
        lock (_sync)
        {
            var state = _tiers[tier];
            // Share the factory when both tiers point at the same file.
            var twin = _tiers[tier == ModelTier.Draft ? ModelTier.Verify : ModelTier.Draft];
            state.Factory ??= twin.Path == state.Path && twin.Factory is not null
                ? twin.Factory
                : WhisperFactory.FromPath(state.Path);
            state.Processor ??= BuildProcessor(state, state.ProcessorLanguage, tier, state.ProcessorAudioCtx);
        }
    }

    private static WhisperProcessor BuildProcessor(TierState state, string language, ModelTier tier, int audioCtx)
    {
        // Transcription task only — WithTranslate is deliberately never called, so the
        // engine can never translate. A manually selected language disables detection
        // for the whole recording; "auto" is the only mode that detects.
        // Conservative decoding (temperature 0, no cross-segment context) reduces
        // hallucinations, repetitions and spontaneous language switches.
        var builder = state.Factory!.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(Math.Max(2, Environment.ProcessorCount / 2)) // physical cores
            .WithTemperature(0f)
            .WithNoContext()
            .WithNoSpeechThreshold(0.6f)
            .WithAudioContextSize(audioCtx)
            .WithProbabilities();
        if (tier == ModelTier.Verify)
        {
            // Beam search lowers WER on the quality tier; the drafter stays greedy for
            // speed. Decode cost is small next to the encoder for short segments.
            if (builder.WithBeamSearchSamplingStrategy() is BeamSearchSamplingStrategyBuilder beam)
            {
                beam.WithBeamSize(5);
            }
        }
        return builder.Build();
    }

    /// <param name="language">"auto" or a forced ISO code ("es", "en", …). A forced code
    /// locks whisper's output language for this call; detection is off.</param>
    /// <param name="tier">Draft (tiny, fast) or Verify (base, accurate).</param>
    public async Task<TranscriptionResult> TranscribeAsync(
        byte[] pcm16kMono16Bit, string language = "auto", ModelTier tier = ModelTier.Verify)
    {
        // No speech energy → no transcription. Prevents silence hallucination at the
        // engine level (the streaming VAD is a second, upstream line of defense).
        if (Rms(pcm16kMono16Bit) < SilenceRmsThreshold)
        {
            return new TranscriptionResult("", language == "auto" ? "?" : language, 0f);
        }

        Preload(tier);

        await _gate.WaitAsync();
        try
        {
            TierState state;
            var audioCtx = AudioContextFor(pcm16kMono16Bit);
            lock (_sync)
            {
                state = _tiers[tier];
                if (state.ProcessorLanguage != language || state.ProcessorAudioCtx != audioCtx)
                {
                    state.Processor?.Dispose();
                    state.Processor = BuildProcessor(state, language, tier, audioCtx);
                    state.ProcessorLanguage = language;
                    state.ProcessorAudioCtx = audioCtx;
                }
            }

            using var wav = BuildWavStream(pcm16kMono16Bit);
            var text = new StringBuilder();
            var detected = "?";
            float probabilitySum = 0;
            var probabilityCount = 0;
            await foreach (var segment in state.Processor!.ProcessAsync(wav))
            {
                text.Append(FilterSegment(segment, language));
                if (detected == "?" && !string.IsNullOrEmpty(segment.Language))
                {
                    detected = segment.Language;
                }
                if (segment.Probability > 0)
                {
                    probabilitySum += segment.Probability;
                    probabilityCount++;
                }
            }
            var confidence = probabilityCount > 0 ? probabilitySum / probabilityCount : 0f;
            // A manual selection is authoritative; detection never overrides it.
            return new TranscriptionResult(
                text.ToString().Trim(),
                language == "auto" ? detected : language,
                confidence);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Faithfulness guard: flags instead of trusting (a) segments whisper itself scored
    /// as low-confidence and (b) output in a script that cannot come from the locked
    /// language (e.g. CJK while Spanish is forced) — both are hallucination signatures.
    /// Individual foreign words in Latin script pass through untouched.
    /// </summary>
    internal static string FilterSegment(SegmentData segment, string language)
    {
        var text = segment.Text;
        if (string.IsNullOrWhiteSpace(text) || IsNonSpeechAnnotation(text))
        {
            return string.Empty;
        }
        if (language != "auto" && IsMostlyForeignScript(text))
        {
            return " [inaudible]";
        }
        // Stray CJK/Cyrillic characters inside otherwise-Latin text are hallucination
        // artifacts (never real dictation) — drop just those characters.
        text = StripStrayForeignChars(text);
        if (segment.Probability > 0 && segment.Probability < LowConfidence)
        {
            return $" [poco claro:{text.TrimEnd()}]";
        }
        return text;
    }

    /// <summary>
    /// Whisper emits non-speech annotations on noise/silence — "[Música]", "(Aplausos)",
    /// "♪♪", "[BLANK_AUDIO]"… They are never dictation; drop the whole segment when it
    /// consists only of such an annotation.
    /// </summary>
    internal static bool IsNonSpeechAnnotation(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }
        if (trimmed.All(ch => ch == '♪' || ch == '♫' || ch == '*' || char.IsWhiteSpace(ch) || ch == '.'))
        {
            return true;
        }
        return (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            || (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
            || (trimmed.StartsWith('*') && trimmed.EndsWith('*'));
    }

    internal static string StripStrayForeignChars(string text)
    {
        int letters = 0, foreign = 0;
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                letters++;
                if (ch >= 0x0400)
                {
                    foreign++;
                }
            }
        }
        // Only strip when clearly stray (a small minority); a genuinely foreign segment
        // is handled by the majority check above or passed through in AUTO mode.
        if (foreign == 0 || foreign * 4 >= letters)
        {
            return text;
        }
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!(char.IsLetter(ch) && ch >= 0x0400))
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    internal static bool IsMostlyForeignScript(string text)
    {
        int letters = 0, foreign = 0;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch))
            {
                continue;
            }
            letters++;
            if (ch >= 0x0400) // beyond Latin/Greek blocks: Cyrillic, CJK, Kana, Hangul…
            {
                foreign++;
            }
        }
        return letters > 0 && foreign * 2 > letters;
    }

    internal static double Rms(byte[] pcm)
    {
        var samples = pcm.Length / 2;
        if (samples == 0)
        {
            return 0;
        }
        double sumSquares = 0;
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = (short)(pcm[i] | (pcm[i + 1] << 8));
            sumSquares += (double)sample * sample;
        }
        return Math.Sqrt(sumSquares / samples) / short.MaxValue;
    }

    private static MemoryStream BuildWavStream(byte[] pcm)
    {
        const int sampleRate = 16000;
        const short bitsPerSample = 16;
        const short channels = 1;
        const int byteRate = sampleRate * channels * bitsPerSample / 8;

        var stream = new MemoryStream(44 + pcm.Length);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + pcm.Length);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write("data"u8);
            writer.Write(pcm.Length);
            writer.Write(pcm);
        }
        stream.Position = 0;
        return stream;
    }

    public void Dispose()
    {
        // Tiers may share a factory; dispose each object once.
        var disposed = new HashSet<object>();
        foreach (var state in _tiers.Values)
        {
            if (state.Processor is not null && disposed.Add(state.Processor))
            {
                state.Processor.Dispose();
            }
            if (state.Factory is not null && disposed.Add(state.Factory))
            {
                state.Factory.Dispose();
            }
        }
        _gate.Dispose();
    }
}
