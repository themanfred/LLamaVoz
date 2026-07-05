using System.Diagnostics;
using System.IO;
using LLamaVoz.DesktopApp.Services;
using LLamaVoz.Evals.Audio;

namespace LLamaVoz.Evals.Categories;

/// <summary>ASR correctness (WER vs reference) and performance (load/RTF) evals.</summary>
public static class AsrEvals
{
    public static IEnumerable<EvalCase> Correctness(
        Lazy<TranscriptionService> service, IReadOnlyDictionary<string, AudioCase> cases)
    {
        // (id, audio case, tier, forced language or auto, WER threshold)
        var matrix = new (string Id, string CaseId, ModelTier Tier, string Lang, double MaxWer)[]
        {
            ("asr-es-base-forced", "test-es", ModelTier.Verify, "es", 0.15),
            ("asr-es-tiny-forced", "test-es", ModelTier.Draft, "es", 0.25),
            ("asr-en-base-forced", "test-en", ModelTier.Verify, "en", 0.15),
            ("asr-en-tiny-forced", "test-en", ModelTier.Draft, "en", 0.25),
            ("asr-numbers-es", "numbers-es", ModelTier.Verify, "es", 0.35),
            ("asr-propernouns-en", "propernouns-en", ModelTier.Verify, "en", 0.25),
            ("asr-paragraph-es", "paragraph-es", ModelTier.Verify, "es", 0.15),
        };

        foreach (var (id, caseId, tier, lang, maxWer) in matrix)
        {
            yield return new EvalCase(id, "asr",
                $"WER de {caseId} con {(tier == ModelTier.Verify ? "base" : "tiny")}, idioma {lang}",
                () => WerCase(service, cases, caseId, tier, lang, maxWer));
        }

        // Auto language detection must identify the language AND still transcribe well.
        foreach (var (id, caseId, expectLang) in new[]
                 { ("asr-es-base-auto", "test-es", "es"), ("asr-en-base-auto", "test-en", "en") })
        {
            yield return new EvalCase(id, "asr",
                $"Detección automática de idioma en {caseId} (esperado: {expectLang})",
                async () =>
                {
                    var audioCase = cases[caseId];
                    var result = await service.Value.TranscribeAsync(
                        WavIo.ReadPcm(audioCase.AbsolutePath), "auto", ModelTier.Verify);
                    var wer = Wer.Compute(audioCase.Reference, result.Text);
                    var metrics = Metrics(("idioma detectado", result.Language), ("WER", $"{wer:P1}"),
                        ("confianza", $"{result.Confidence:F2}"));
                    if (result.Language != expectLang)
                    {
                        return CaseResult.Fail($"Idioma detectado '{result.Language}', esperado '{expectLang}'.", metrics);
                    }
                    return wer <= 0.15
                        ? CaseResult.Pass(null, metrics)
                        : CaseResult.Fail($"WER {wer:P1} > 15%. Hipótesis: \"{result.Text}\"", metrics);
                });
        }

        yield return new EvalCase("asr-silence", "asr",
            "5 s de silencio puro no deben producir texto (guarda anti-alucinación)",
            async () =>
            {
                var result = await service.Value.TranscribeAsync(WavIo.Silence(5.0), "es", ModelTier.Verify);
                var metrics = Metrics(("salida", result.Text.Length == 0 ? "(vacía)" : $"\"{result.Text}\""));
                return string.IsNullOrWhiteSpace(result.Text)
                    ? CaseResult.Pass(null, metrics)
                    : CaseResult.Fail($"El silencio produjo texto alucinado: \"{result.Text}\"", metrics);
            });

        yield return new EvalCase("asr-short-clip", "asr",
            "Clip ultracorto (~0.5 s, \"sí\") no debe romper el motor",
            async () =>
            {
                var audioCase = cases["short-es"];
                if (!File.Exists(audioCase.AbsolutePath))
                {
                    return CaseResult.Skip("WAV no generado (voz TTS no disponible).");
                }
                var result = await service.Value.TranscribeAsync(
                    WavIo.ReadPcm(audioCase.AbsolutePath), "es", ModelTier.Verify);
                return CaseResult.Pass(null, Metrics(("salida", $"\"{result.Text}\""), ("confianza", $"{result.Confidence:F2}")));
            });

        yield return new EvalCase("asr-lang-mismatch", "asr",
            "Audio inglés con idioma forzado a 'es': la salida debe quedar marcada, no inventada con confianza",
            async () =>
            {
                var audioCase = cases["test-en"];
                var result = await service.Value.TranscribeAsync(
                    WavIo.ReadPcm(audioCase.AbsolutePath), "es", ModelTier.Verify);
                var flagged = result.Text.Contains("[inaudible]") || result.Text.Contains("[poco claro");
                var lowConfidence = result.Confidence < 0.85f;
                var metrics = Metrics(("salida", $"\"{Truncate(result.Text, 120)}\""),
                    ("confianza", $"{result.Confidence:F2}"), ("marcada", flagged.ToString()));
                return flagged || lowConfidence || string.IsNullOrWhiteSpace(result.Text)
                    ? CaseResult.Pass("La salida quedó marcada o con confianza baja (comportamiento esperado).", metrics)
                    : CaseResult.Fail(
                        "El motor produjo texto con confianza alta pese al idioma equivocado — la guarda no saltó.", metrics);
            });
    }

    public static IEnumerable<EvalCase> Performance(
        Lazy<TranscriptionService> service, IReadOnlyDictionary<string, AudioCase> cases)
    {
        yield return new EvalCase("perf-load-tiny", "perf",
            "Carga del modelo tiny (caché de SO caliente) < 2 s",
            () => Task.FromResult(TimeLoad(ModelTier.Draft, 2.0)));

        yield return new EvalCase("perf-load-base", "perf",
            "Carga del modelo base (caché de SO caliente) < 3 s",
            () => Task.FromResult(TimeLoad(ModelTier.Verify, 3.0)));

        foreach (var (id, tier, maxRtf) in new[]
                 { ("perf-rtf-tiny", ModelTier.Draft, 0.5), ("perf-rtf-base", ModelTier.Verify, 1.0) })
        {
            yield return new EvalCase(id, "perf",
                $"Factor de tiempo real de {(tier == ModelTier.Draft ? "tiny" : "base")} ≤ {maxRtf:F1}x",
                async () =>
                {
                    var pcm = WavIo.ReadPcm(cases["test-es"].AbsolutePath);
                    await service.Value.TranscribeAsync(pcm, "es", tier); // warm processor for this tier+lang
                    var timer = Stopwatch.StartNew();
                    await service.Value.TranscribeAsync(pcm, "es", tier);
                    timer.Stop();
                    var rtf = timer.Elapsed.TotalSeconds / WavIo.Seconds(pcm);
                    var metrics = Metrics(("RTF", $"{rtf:F2}x"), ("umbral", $"{maxRtf:F1}x"));
                    return rtf <= maxRtf
                        ? CaseResult.Pass(null, metrics)
                        : CaseResult.Fail($"RTF {rtf:F2}x supera el umbral {maxRtf:F1}x.", metrics);
                });
        }
    }

    private static CaseResult TimeLoad(ModelTier tier, double maxSeconds)
    {
        using var fresh = new TranscriptionService();
        var timer = Stopwatch.StartNew();
        fresh.Preload(tier);
        timer.Stop();
        var metrics = Metrics(("carga", $"{timer.Elapsed.TotalMilliseconds:F0} ms"), ("umbral", $"{maxSeconds:F0} s"));
        return timer.Elapsed.TotalSeconds <= maxSeconds
            ? CaseResult.Pass(null, metrics)
            : CaseResult.Fail($"Carga tardó {timer.Elapsed.TotalSeconds:F1} s.", metrics);
    }

    private static async Task<CaseResult> WerCase(
        Lazy<TranscriptionService> service, IReadOnlyDictionary<string, AudioCase> cases,
        string caseId, ModelTier tier, string lang, double maxWer)
    {
        var audioCase = cases[caseId];
        if (!File.Exists(audioCase.AbsolutePath))
        {
            return CaseResult.Skip("WAV no generado (voz TTS no disponible).");
        }
        var pcm = WavIo.ReadPcm(audioCase.AbsolutePath);
        var result = await service.Value.TranscribeAsync(pcm, lang, tier);
        var wer = Wer.Compute(audioCase.Reference, result.Text);
        var metrics = Metrics(("WER", $"{wer:P1}"), ("umbral", $"{maxWer:P0}"),
            ("confianza", $"{result.Confidence:F2}"), ("audio", $"{WavIo.Seconds(pcm):F1} s"));
        if (wer <= maxWer)
        {
            return CaseResult.Pass(null, metrics);
        }
        metrics["referencia"] = audioCase.Reference;
        metrics["hipótesis"] = result.Text;
        return CaseResult.Fail($"WER {wer:P1} supera el umbral {maxWer:P0}.", metrics);
    }

    private static Dictionary<string, string> Metrics(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
