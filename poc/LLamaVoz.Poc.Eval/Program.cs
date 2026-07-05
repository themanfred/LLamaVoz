// LLamaVoz eval harness: synthesizes known text with the local Windows TTS voices,
// runs it through the real transcription pipeline (both model tiers and the streaming
// VAD session in its three quality modes), and reports WER / latency / confidence.
// Fully local — no audio or text ever leaves the machine.
//
// Caveat printed in the report: TTS audio is cleaner than a human microphone, so WER
// here is a lower bound; the numbers are for comparing tiers/modes, not absolute truth.

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using LLamaVoz.DesktopApp.Services;

const int SampleRate = 16000;
const int BytesPerSecond = SampleRate * 2;

var cases = new EvalCase[]
{
    new("es-simple", "es", "es",
        "Esto se ve mucho mejor, no hay ni comparación."),
    new("es-numeros", "es", "es",
        "La reunión es el catorce de marzo a las tres y media y costará dos mil quinientos euros."),
    new("es-nombres", "es", "es",
        "Mi amigo Joaquín acaba de tener un hijo que se llama Antonio."),
    new("es-coloquial", "es", "es",
        "Ahora mismo estoy hablando, voy a ver si esto tiene sentido o realmente está equivocándose."),
    new("en-simple", "en", "en",
        "I am looking at a beautiful skyline and the sun is shining."),
    new("en-names", "en", "en",
        "My friend Kurt wants a formal email congratulating him for his achievements."),
    new("auto-es", "es", "auto",
        "Quiero comprobar que la detección automática elige español y no lo mezcla."),
    new("auto-en", "en", "auto",
        "Let us check that automatic detection picks English and keeps it."),
};

var report = new StringBuilder();
report.AppendLine("# LLamaVoz — Informe de evaluación");
report.AppendLine();
report.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm} · Máquina: {Environment.MachineName} · CPU lógicas: {Environment.ProcessorCount}");
report.AppendLine();
report.AppendLine("> Audio generado con las voces TTS locales de Windows (System.Speech): más limpio que un micrófono real, " +
                  "así que el WER es un límite inferior. Sirve para comparar niveles y modos, no como verdad absoluta.");
report.AppendLine();

// --- Voices -----------------------------------------------------------------
using var synth = new SpeechSynthesizer();
var voices = synth.GetInstalledVoices().Where(v => v.Enabled).ToList();
report.AppendLine("## Voces TTS disponibles");
report.AppendLine();
foreach (var v in voices)
{
    report.AppendLine($"- {v.VoiceInfo.Name} ({v.VoiceInfo.Culture.Name})");
}
report.AppendLine();

string? VoiceFor(string lang) => voices
    .FirstOrDefault(v => v.VoiceInfo.Culture.TwoLetterISOLanguageName == lang)?.VoiceInfo.Name;

byte[]? Synthesize(string text, string lang)
{
    var voice = VoiceFor(lang);
    if (voice is null)
    {
        return null;
    }
    synth.SelectVoice(voice);
    using var ms = new MemoryStream();
    synth.SetOutputToAudioStream(ms,
        new SpeechAudioFormatInfo(SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
    synth.Speak(text);
    synth.SetOutputToNull();
    return ms.ToArray(); // raw 16 kHz mono PCM, same as the mic path
}

// --- 1. Tier comparison (Draft=tiny vs Verify=base+beam) ---------------------
Console.WriteLine("Cargando modelos…");
using var svc = new TranscriptionService();
svc.Preload(ModelTier.Draft);
svc.Preload(ModelTier.Verify);
Console.WriteLine($"Draft:  {svc.DraftModelPath}");
Console.WriteLine($"Verify: {svc.ModelPath}");

report.AppendLine("## 1. Comparación de niveles por caso (audio de una pieza)");
report.AppendLine();
report.AppendLine($"Draft = `{Path.GetFileName(svc.DraftModelPath)}` (greedy) · Verify = `{Path.GetFileName(svc.ModelPath)}` (beam 5)");
report.AppendLine();
report.AppendLine("| Caso | Idioma | Nivel | WER | Conf. | Latencia | Audio | Detectado | Salida |");
report.AppendLine("|---|---|---|---|---|---|---|---|---|");

foreach (var c in cases)
{
    var pcm = Synthesize(c.Text, c.Culture);
    if (pcm is null || pcm.Length < BytesPerSecond / 2)
    {
        report.AppendLine($"| {c.Id} | {c.Lang} | — | — | — | — | — | — | (sin voz TTS {c.Culture}) |");
        continue;
    }
    var seconds = pcm.Length / (double)BytesPerSecond;
    foreach (var tier in new[] { ModelTier.Draft, ModelTier.Verify })
    {
        var sw = Stopwatch.StartNew();
        var result = await svc.TranscribeAsync(pcm, c.Lang, tier);
        sw.Stop();
        var wer = Wer(c.Text, result.Text);
        report.AppendLine(
            $"| {c.Id} | {c.Lang} | {tier} | {wer:P0} | {result.Confidence:F2} | {sw.ElapsedMilliseconds} ms | " +
            $"{seconds:F1} s | {result.Language} | {Shorten(result.Text)} |");
        Console.WriteLine($"{c.Id,-14} {tier,-7} WER {wer:P0}  conf {result.Confidence:F2}  {sw.ElapsedMilliseconds} ms");
    }
}
report.AppendLine();

// --- 2. Streaming session end-to-end (VAD + modes) ---------------------------
report.AppendLine("## 2. Sesión de streaming (VAD + modos de calidad)");
report.AppendLine();
report.AppendLine("Dos frases con una pausa de 0,9 s entre ellas, entregadas en chunks de 50 ms como el micrófono real. " +
                  "La latencia mostrada es el total de FinishAsync sin tiempo real de habla (peor caso; en uso real los " +
                  "segmentos previos ya están transcritos al soltar la tecla).");
report.AppendLine();
report.AppendLine("| Escenario | Modo | WER | Latencia total | Segmentos | Verificados | Idioma | Salida |");
report.AppendLine("|---|---|---|---|---|---|---|---|");

var streamingScenarios = new (string Id, string Culture, string Lang, string A, string B)[]
{
    ("es-stream", "es", "es",
        "Primero digo una frase completa con contenido claro.",
        "Después de una pausa digo la segunda parte y no debería perderse nada."),
    ("en-stream", "en", "auto",
        "First I say one complete sentence with clear content.",
        "After a pause I say the second part and nothing should be lost."),
};

foreach (var s in streamingScenarios)
{
    var a = Synthesize(s.A, s.Culture);
    var b = Synthesize(s.B, s.Culture);
    if (a is null || b is null)
    {
        report.AppendLine($"| {s.Id} | — | — | — | — | — | — | (sin voz TTS {s.Culture}) |");
        continue;
    }
    var silence = new byte[(int)(BytesPerSecond * 0.9)];
    var full = a.Concat(silence).Concat(b).ToArray();
    var reference = $"{s.A} {s.B}";

    foreach (var mode in new[] { "accurate", "balanced", "fast" })
    {
        var session = new StreamingTranscriptionSession(svc, s.Lang, mode);
        for (var offset = 0; offset < full.Length; offset += 1600)
        {
            session.AddChunk(full.Skip(offset).Take(1600).ToArray());
        }
        var sw = Stopwatch.StartNew();
        var (text, language) = await session.FinishAsync();
        sw.Stop();
        var wer = Wer(reference, text);
        report.AppendLine(
            $"| {s.Id} | {mode} | {wer:P0} | {sw.ElapsedMilliseconds} ms | {session.SegmentsProcessed} | " +
            $"{session.VerifiedCount} | {language} | {Shorten(text)} |");
        Console.WriteLine($"{s.Id,-11} {mode,-9} WER {wer:P0}  {sw.ElapsedMilliseconds} ms  segs {session.SegmentsProcessed} verif {session.VerifiedCount}");
    }
}
report.AppendLine();

// --- 3. Filter unit checks (via reflection on the private helpers) -----------
report.AppendLine("## 3. Filtros anti-alucinación (comprobaciones unitarias)");
report.AppendLine();
report.AppendLine("| Comprobación | Esperado | Resultado |");
report.AppendLine("|---|---|---|");

var svcType = typeof(TranscriptionService);
var isAnnotation = svcType.GetMethod("IsNonSpeechAnnotation", BindingFlags.NonPublic | BindingFlags.Static)!;
var stripStray = svcType.GetMethod("StripStrayForeignChars", BindingFlags.NonPublic | BindingFlags.Static)!;
var isForeign = svcType.GetMethod("IsMostlyForeignScript", BindingFlags.NonPublic | BindingFlags.Static)!;

var filterChecks = new (string Name, Func<object?> Run, object Expected)[]
{
    ("anotación [Música] se descarta", () => isAnnotation.Invoke(null, new object[] { " [Música]" }), true),
    ("anotación (Aplausos) se descarta", () => isAnnotation.Invoke(null, new object[] { "(Aplausos)" }), true),
    ("anotación ♪♪ se descarta", () => isAnnotation.Invoke(null, new object[] { " ♪♪ " }), true),
    ("frase normal NO es anotación", () => isAnnotation.Invoke(null, new object[] { "hola mundo" }), false),
    ("carácter CJK suelto se elimina", () => stripStray.Invoke(null, new object[] { "lots of chicken 炙 wings" }), "lots of chicken  wings"),
    ("texto latino queda intacto", () => stripStray.Invoke(null, new object[] { "todo normal aquí" }), "todo normal aquí"),
    ("segmento mayoritariamente CJK detectado", () => isForeign.Invoke(null, new object[] { "全部中文内容在这里" }), true),
    ("préstamo latino ('software okay') pasa", () => isForeign.Invoke(null, new object[] { "esto es software okay" }), false),
};

var filterFailures = 0;
foreach (var (name, run, expected) in filterChecks)
{
    var actual = run();
    var pass = Equals(actual, expected);
    if (!pass)
    {
        filterFailures++;
    }
    report.AppendLine($"| {name} | `{expected}` | {(pass ? "✅" : $"❌ `{actual}`")} |");
}
report.AppendLine();

// --- Summary ------------------------------------------------------------------
report.AppendLine("## Resumen");
report.AppendLine();
report.AppendLine($"- Comprobaciones de filtros: {filterChecks.Length - filterFailures}/{filterChecks.Length} correctas.");
report.AppendLine("- Ver tablas 1 y 2 para WER/latencia por nivel y modo.");
report.AppendLine();
report.AppendLine("_Generado por `poc/LLamaVoz.Poc.Eval` — totalmente local._");

var reportPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EVAL-RESULTS.md"));
File.WriteAllText(reportPath, report.ToString());
Console.WriteLine($"\nInforme escrito en {reportPath}");
Console.WriteLine($"Filtros: {filterChecks.Length - filterFailures}/{filterChecks.Length} OK");
return filterFailures == 0 ? 0 : 1;

static string[] Words(string s) => new string(s.ToLowerInvariant()
        .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray())
    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

static double Wer(string reference, string hypothesis)
{
    var r = Words(reference);
    var h = Words(hypothesis);
    if (r.Length == 0)
    {
        return h.Length == 0 ? 0 : 1;
    }
    var d = new int[r.Length + 1, h.Length + 1];
    for (var i = 0; i <= r.Length; i++) d[i, 0] = i;
    for (var j = 0; j <= h.Length; j++) d[0, j] = j;
    for (var i = 1; i <= r.Length; i++)
    {
        for (var j = 1; j <= h.Length; j++)
        {
            var cost = r[i - 1] == h[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
        }
    }
    return (double)d[r.Length, h.Length] / r.Length;
}

static string Shorten(string s)
{
    var clean = s.Replace("|", "/").Replace("\n", " ");
    return clean.Length <= 90 ? clean : clean[..90] + "…";
}

internal sealed record EvalCase(string Id, string Culture, string Lang, string Text);
