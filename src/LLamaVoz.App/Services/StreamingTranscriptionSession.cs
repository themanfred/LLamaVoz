using System.Diagnostics;
using LLamaVoz.Audio;

namespace LLamaVoz.DesktopApp.Services;

/// <summary>
/// Streams one dictation: receives live PCM chunks, cuts segments at speech pauses
/// (energy VAD) and transcribes each segment in the background while the user keeps
/// talking. At key-release only the trailing segment remains to transcribe, so the
/// perceived wait drops from (total audio × RTF) to (last segment × RTF).
///
/// Speculative transcription (DSpark-style draft→verify): in "balanced" mode each
/// segment is first drafted with the tiny model; only drafts whose whisper confidence
/// falls below a threshold are re-transcribed with the base model. The threshold is
/// load-adaptive: when segments queue up (long continuous speech) the bar for paying
/// the expensive verification drops, so the backlog drains instead of growing.
/// Audio only ever exists in memory (NFR-09).
/// </summary>
public sealed class StreamingTranscriptionSession
{
    // Raw RMS (0..1, no gain) above which a 50 ms chunk counts as speech.
    private const double SpeechRmsThreshold = 0.015;
    // ~600 ms of continuous silence after speech ends a segment.
    private const int SilenceChunksToCut = 12;
    // Never hand whisper a segment shorter than ~2 s — short clips give the model too
    // little context and per-call overhead dominates.
    private const int MinSegmentBytes = 2 * MicrophoneRecorder.BytesPerSecond;
    // Continuous speech without a 600 ms pause would otherwise grow one giant trailing
    // segment and reintroduce the batch-latency problem (NFR-02). After 4 s, any brief
    // energy dip (a word gap) is good enough to cut — whisper is single-flight, so only
    // small segments (with audio_ctx capped to their length) keep the tail short.
    // At 10 s we cut unconditionally: a rare mid-word split at the boundary costs less
    // than seconds of tail latency.
    private const int SoftCutBytes = 4 * MicrophoneRecorder.BytesPerSecond;
    private const int HardCutBytes = 10 * MicrophoneRecorder.BytesPerSecond;
    // Accept a tiny draft outright when its mean probability reaches this. Calibrated
    // with the TTS eval (poc/EVAL-RESULTS.md): correct drafts scored ≥0.85, drafts with
    // real errors scored 0.76–0.83, so 0.85 separates them cleanly on that set.
    private const float AcceptDraftConfidence = 0.85f;
    // Under backlog (≥2 segments in flight) verification is reserved for clearly bad
    // drafts so the queue drains — the DSpark load-aware budget, in miniature.
    private const float AcceptDraftConfidenceUnderLoad = 0.80f;
    private const int BacklogThreshold = 2;

    private readonly TranscriptionService _transcription;
    private readonly string _language; // locked for the whole session (manual selection wins)
    private readonly string _qualityMode; // "fast" | "balanced" | "accurate"
    private readonly object _sync = new();
    private readonly List<byte> _segment = new();
    private readonly List<Task<TranscriptionResult>> _pending = new();
    // In AUTO mode, the language detected on the first segment locks the rest of the
    // session — otherwise each VAD segment detects independently and one dictation can
    // flip between languages mid-sentence.
    private volatile string? _detectedLanguage;
    private int _inFlight;
    private int _verifiedCount;
    private int _silentChunks;
    private bool _speechInSegment;
    private bool _closed;

    /// <summary>Segments cut and sent to transcription so far (evals/diagnostics).</summary>
    internal int SegmentsProcessed { get { lock (_sync) { return _pending.Count; } } }

    /// <summary>Segments re-transcribed with the Verify tier in balanced mode (evals/diagnostics).</summary>
    internal int VerifiedCount => Volatile.Read(ref _verifiedCount);

    /// <summary>Language locked by the first non-empty segment in AUTO mode (evals/diagnostics).</summary>
    internal string? DetectedLanguage => _detectedLanguage;

    public StreamingTranscriptionSession(
        TranscriptionService transcription, string language = "auto", string qualityMode = "balanced")
    {
        _transcription = transcription;
        _language = language;
        // Balanced needs two distinct models; with a single model fall back to accurate.
        _qualityMode = qualityMode == "balanced" && !transcription.HasDistinctTiers
            ? "accurate"
            : qualityMode;
    }

    public void AddChunk(byte[] chunk)
    {
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            _segment.AddRange(chunk);

            var chunkIsQuiet = ChunkRms(chunk) < SpeechRmsThreshold;
            if (chunkIsQuiet)
            {
                _silentChunks++;
            }
            else
            {
                _speechInSegment = true;
                _silentChunks = 0;
            }

            var pauseCut = _silentChunks >= SilenceChunksToCut && _segment.Count >= MinSegmentBytes;
            var softCut = _segment.Count >= SoftCutBytes && chunkIsQuiet;
            var hardCut = _segment.Count >= HardCutBytes;
            if (_speechInSegment && (pauseCut || softCut || hardCut))
            {
                CutSegmentLocked();
            }
        }
    }

    /// <summary>Flushes the trailing segment, awaits all in-flight transcriptions and joins them.</summary>
    public async Task<(string Text, string Language)> FinishAsync()
    {
        Task<TranscriptionResult>[] tasks;
        lock (_sync)
        {
            _closed = true;
            if (_speechInSegment)
            {
                CutSegmentLocked();
            }
            _segment.Clear();
            tasks = _pending.ToArray();
        }

        var results = await Task.WhenAll(tasks);

        var language = "?";
        var parts = new List<string>(results.Length);
        foreach (var result in results)
        {
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                parts.Add(result.Text.Trim());
            }
            if (language == "?" && result.Language != "?")
            {
                language = result.Language;
            }
        }
        Debug.WriteLine(
            $"[LLamaVoz] dictado: {results.Length} segmentos, {_verifiedCount} verificados con base (modo {_qualityMode})");
        return (string.Join(" ", parts), language);
    }

    /// <summary>Stops accepting audio and discards buffered/in-flight work (results ignored).</summary>
    public void Cancel()
    {
        lock (_sync)
        {
            _closed = true;
            _segment.Clear();
        }
    }

    private void CutSegmentLocked()
    {
        var pcm = _segment.ToArray();
        _segment.Clear();
        _speechInSegment = false;
        _silentChunks = 0;
        Interlocked.Increment(ref _inFlight);
        _pending.Add(Task.Run(() => ProcessSegmentAsync(pcm)));
    }

    private async Task<TranscriptionResult> ProcessSegmentAsync(byte[] pcm)
    {
        try
        {
            // Once AUTO has detected a language, lock it for the rest of the session.
            var language = _language == "auto" ? _detectedLanguage ?? "auto" : _language;

            if (_qualityMode == "accurate")
            {
                return LockLanguage(await _transcription.TranscribeAsync(pcm, language, ModelTier.Verify));
            }

            var draft = LockLanguage(await _transcription.TranscribeAsync(pcm, language, ModelTier.Draft));
            if (_qualityMode == "fast")
            {
                return draft;
            }

            // Confidence-scheduled verification: pay for the base model only when the
            // draft is not trustworthy. Empty drafts (pure noise) are accepted as-is.
            var threshold = Volatile.Read(ref _inFlight) > BacklogThreshold
                ? AcceptDraftConfidenceUnderLoad
                : AcceptDraftConfidence;
            if (string.IsNullOrWhiteSpace(draft.Text) || draft.Confidence >= threshold)
            {
                return draft;
            }

            Interlocked.Increment(ref _verifiedCount);
            return LockLanguage(await _transcription.TranscribeAsync(pcm, language, ModelTier.Verify));
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private TranscriptionResult LockLanguage(TranscriptionResult result)
    {
        if (_detectedLanguage is null && result.Language != "?" && !string.IsNullOrWhiteSpace(result.Text))
        {
            _detectedLanguage = result.Language;
        }
        return result;
    }

    private static double ChunkRms(byte[] chunk)
    {
        var samples = chunk.Length / 2;
        if (samples == 0)
        {
            return 0;
        }
        double sumSquares = 0;
        for (var i = 0; i + 1 < chunk.Length; i += 2)
        {
            var sample = (short)(chunk[i] | (chunk[i + 1] << 8));
            sumSquares += (double)sample * sample;
        }
        return Math.Sqrt(sumSquares / samples) / short.MaxValue;
    }
}
