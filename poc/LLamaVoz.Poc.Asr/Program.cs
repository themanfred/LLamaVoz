using System.Diagnostics;
using Whisper.net;

// POC-2 harness (IMPLEMENTATION_PLAN.md M2): measure local ASR latency and quality.
// Usage: LLamaVoz.Poc.Asr <model.bin> <audio1.wav> [audio2.wav ...]
// WAVs must be 16 kHz, 16-bit, mono.

if (args.Length < 2)
{
    Console.WriteLine("Uso: LLamaVoz.Poc.Asr <modelo.bin> <audio1.wav> [audio2.wav ...]");
    return 2;
}

var modelPath = args[0];
if (!File.Exists(modelPath))
{
    Console.WriteLine($"Modelo no encontrado: {modelPath}");
    return 2;
}

Console.WriteLine($"Modelo: {Path.GetFileName(modelPath)} ({new FileInfo(modelPath).Length / 1024 / 1024} MB)");
Console.WriteLine($"CPU: {Environment.ProcessorCount} núcleos lógicos");

var loadTimer = Stopwatch.StartNew();
using var factory = WhisperFactory.FromPath(modelPath);
using var processor = factory.CreateBuilder()
    .WithLanguage("auto")
    .WithThreads(Environment.ProcessorCount)
    .Build();
loadTimer.Stop();
Console.WriteLine($"Carga del modelo: {loadTimer.ElapsedMilliseconds} ms\n");

var exitCode = 0;
foreach (var wavPath in args.Skip(1))
{
    if (!File.Exists(wavPath))
    {
        Console.WriteLine($"Audio no encontrado: {wavPath}");
        exitCode = 1;
        continue;
    }

    // 16 kHz * 16-bit mono = 32,000 bytes/s; header ≈ 44 bytes.
    var audioSeconds = (new FileInfo(wavPath).Length - 44) / 32000.0;

    Console.WriteLine($"--- {Path.GetFileName(wavPath)} ({audioSeconds:F1} s de audio) ---");
    var timer = Stopwatch.StartNew();
    var firstSegmentMs = -1L;
    var segments = new List<SegmentData>();

    await using (var stream = File.OpenRead(wavPath))
    {
        await foreach (var segment in processor.ProcessAsync(stream))
        {
            if (firstSegmentMs < 0)
            {
                firstSegmentMs = timer.ElapsedMilliseconds;
            }
            segments.Add(segment);
        }
    }
    timer.Stop();

    var transcript = string.Concat(segments.Select(s => s.Text)).Trim();
    var language = segments.FirstOrDefault()?.Language ?? "?";

    Console.WriteLine($"Idioma detectado: {language}");
    Console.WriteLine($"Transcripción:    {transcript}");
    Console.WriteLine($"Primer segmento:  {firstSegmentMs} ms");
    Console.WriteLine($"Tiempo total:     {timer.ElapsedMilliseconds} ms " +
                      $"(factor tiempo real: {timer.ElapsedMilliseconds / 1000.0 / audioSeconds:F2}x)");
    Console.WriteLine();
}

return exitCode;
