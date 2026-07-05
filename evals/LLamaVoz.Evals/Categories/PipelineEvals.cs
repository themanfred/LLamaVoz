using LLamaVoz.DesktopApp.Services;
using LLamaVoz.Evals.Audio;
using LLamaVoz.Insertion;

namespace LLamaVoz.Evals.Categories;

/// <summary>
/// End-to-end product path minus microphone/hotkey: WAV chunks → streaming session →
/// insertion engine → own test window. Verifies the text that actually lands in the field.
/// </summary>
public static class PipelineEvals
{
    public static IEnumerable<EvalCase> All(
        Lazy<TranscriptionService> service, IReadOnlyDictionary<string, AudioCase> cases)
    {
        yield return new EvalCase("e2e-dictation-es", "pipeline",
            "Audio es → sesión streaming → inserción → texto correcto en el campo",
            async () =>
            {
                var audioCase = cases["test-es"];
                var session = new StreamingTranscriptionSession(service.Value, "es", "accurate");
                foreach (var chunk in WavIo.Chunks(WavIo.ReadPcm(audioCase.AbsolutePath)))
                {
                    session.AddChunk(chunk);
                }
                var (text, language) = await session.FinishAsync();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return CaseResult.Fail("La sesión de streaming no produjo texto.");
                }

                using var host = TestWindowHost.Start();
                if (!host.WaitReady(TimeSpan.FromSeconds(5)))
                {
                    return CaseResult.Fail("La ventana de prueba no arrancó.");
                }
                if (!InsertionEvals.TryFocus(host.WindowHandle))
                {
                    return CaseResult.Skip("Escritorio en uso: no se pudo enfocar la ventana de prueba.");
                }

                using var engine = new InsertionEngine();
                var result = engine.Insert(text, TargetWindowGuard.ForWindow(host.WindowHandle));
                Thread.Sleep(600);
                var inserted = host.GetTextBoxText();
                var wer = Wer.Compute(audioCase.Reference, inserted);

                var metrics = new Dictionary<string, string>
                {
                    ["WER del texto insertado"] = $"{wer:P1}",
                    ["idioma"] = language,
                    ["método de inserción"] = result.MethodUsed.ToString(),
                    ["insertado"] = $"\"{inserted}\"",
                };
                if (!result.Success)
                {
                    return CaseResult.Fail($"La inserción falló: {result.Detail}", metrics);
                }
                return wer <= 0.15
                    ? CaseResult.Pass(null, metrics)
                    : CaseResult.Fail($"WER del texto insertado {wer:P1} > 15%.", metrics);
            });
    }
}
