using System.Diagnostics;
using LLamaVoz.DesktopApp.Services;
using LLamaVoz.Evals.Audio;

namespace LLamaVoz.Evals.Categories;

/// <summary>Evals of the streaming session: VAD segmentation, tier routing, language lock, tail latency.</summary>
public static class StreamingEvals
{
    public static IEnumerable<EvalCase> All(
        Lazy<TranscriptionService> service, IReadOnlyDictionary<string, AudioCase> cases)
    {
        yield return new EvalCase("stream-vad-segmentation", "streaming",
            "El VAD corta en ≥2 segmentos un audio con pausa de 1.2 s y el texto combinado es correcto",
            async () =>
            {
                var voice = WavIo.ReadPcm(cases["test-es"].AbsolutePath);
                var combined = WavIo.Concat(voice, WavIo.Silence(1.2), voice, WavIo.Silence(0.8));
                var session = new StreamingTranscriptionSession(service.Value, "es", "accurate");
                foreach (var chunk in WavIo.Chunks(combined))
                {
                    session.AddChunk(chunk);
                }
                var (text, _) = await session.FinishAsync();
                var reference = cases["test-es"].Reference + " " + cases["test-es"].Reference;
                var wer = Wer.Compute(reference, text);
                var metrics = Metrics(("segmentos", session.SegmentsProcessed.ToString()), ("WER combinado", $"{wer:P1}"));
                if (session.SegmentsProcessed < 2)
                {
                    return CaseResult.Fail(
                        $"Solo {session.SegmentsProcessed} segmento(s): el VAD no cortó en la pausa de 1.2 s.", metrics);
                }
                return wer <= 0.20
                    ? CaseResult.Pass(null, metrics)
                    : CaseResult.Fail($"WER combinado {wer:P1} > 20%. Texto: \"{text}\"", metrics);
            });

        yield return new EvalCase("stream-tier-routing", "streaming",
            "En modo balanced los contadores draft/verify cuadran y el resultado no queda vacío",
            async () =>
            {
                if (!service.Value.HasDistinctTiers)
                {
                    return CaseResult.Skip("Solo hay un modelo instalado; balanced degrada a accurate.");
                }
                var pcm = WavIo.ReadPcm(cases["paragraph-es"].AbsolutePath);
                var session = new StreamingTranscriptionSession(service.Value, "es", "balanced");
                foreach (var chunk in WavIo.Chunks(pcm))
                {
                    session.AddChunk(chunk);
                }
                var (text, _) = await session.FinishAsync();
                var segments = session.SegmentsProcessed;
                var verified = session.VerifiedCount;
                var metrics = Metrics(("segmentos", segments.ToString()),
                    ("verificados con base", verified.ToString()),
                    ("aceptados como draft", (segments - verified).ToString()));
                if (segments < 1 || string.IsNullOrWhiteSpace(text))
                {
                    return CaseResult.Fail("La sesión no produjo segmentos o texto.", metrics);
                }
                return verified <= segments
                    ? CaseResult.Pass(null, metrics)
                    : CaseResult.Fail($"Contador inconsistente: {verified} verificados > {segments} segmentos.", metrics);
            });

        yield return new EvalCase("stream-auto-lock", "streaming",
            "En AUTO, el idioma detectado en el primer segmento queda bloqueado para la sesión",
            async () =>
            {
                var pcm = WavIo.ReadPcm(cases["test-es"].AbsolutePath);
                var session = new StreamingTranscriptionSession(service.Value, "auto", "accurate");
                foreach (var chunk in WavIo.Chunks(pcm))
                {
                    session.AddChunk(chunk);
                }
                var (text, language) = await session.FinishAsync();
                var metrics = Metrics(("idioma sesión", language),
                    ("idioma bloqueado", session.DetectedLanguage ?? "(ninguno)"));
                return language == "es" && session.DetectedLanguage == "es" && !string.IsNullOrWhiteSpace(text)
                    ? CaseResult.Pass(null, metrics)
                    : CaseResult.Fail($"Esperado bloqueo en 'es'; sesión='{language}', bloqueado='{session.DetectedLanguage}'.", metrics);
            });

        yield return new EvalCase("perf-tail-latency", "perf",
            "Alimentando audio en tiempo real, la espera tras soltar la tecla (FinishAsync) es < 2.5 s",
            async () =>
            {
                var pcm = WavIo.ReadPcm(cases["test-es"].AbsolutePath);
                var session = new StreamingTranscriptionSession(service.Value, "es", "accurate");
                foreach (var chunk in WavIo.Chunks(pcm))
                {
                    session.AddChunk(chunk);
                    await Task.Delay(50); // real-time feed: VAD works while "the user speaks"
                }
                var timer = Stopwatch.StartNew();
                var (text, _) = await session.FinishAsync();
                timer.Stop();
                var tail = timer.Elapsed.TotalSeconds;
                var metrics = Metrics(("cola tras soltar", $"{tail:F2} s"), ("umbral", "2.5 s"),
                    ("segmentos", session.SegmentsProcessed.ToString()));
                if (string.IsNullOrWhiteSpace(text))
                {
                    return CaseResult.Fail("La sesión en tiempo real no produjo texto.", metrics);
                }
                return tail <= 2.5
                    ? CaseResult.Pass(null, metrics)
                    : CaseResult.Fail($"Cola de {tail:F2} s tras soltar la tecla (objetivo NFR-02 ≤ 1.5 s, umbral eval 2.5 s).", metrics);
            });
    }

    private static Dictionary<string, string> Metrics(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}
