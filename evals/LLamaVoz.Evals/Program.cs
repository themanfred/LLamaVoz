using System.Diagnostics;
using System.IO;
using LLamaVoz.DesktopApp.Services;
using LLamaVoz.Evals;
using LLamaVoz.Evals.Audio;
using LLamaVoz.Evals.Categories;

// LLamaVoz eval suite. Usage:
//   dotnet run --project evals/LLamaVoz.Evals -- [asr|perf|streaming|insertion|pipeline|unit ...]
// Default: all categories. Writes EVALS-REPORT.md at the repo root; exit 1 if any FAIL.

var requested = args.Length == 0 || args.Contains("all")
    ? new[] { "unit", "asr", "perf", "streaming", "insertion", "pipeline" }
    : args.Select(a => a.ToLowerInvariant()).ToArray();

Console.WriteLine("LLamaVoz — Suite de Evals");
Console.WriteLine($"Raíz del repo: {RepoPaths.Root}");
Console.WriteLine($"Categorías: {string.Join(", ", requested)}\n");

// LLAMAVOZ_MODEL would silently pin both tiers to one model — evals need the real setup.
if (Environment.GetEnvironmentVariable("LLAMAVOZ_MODEL") is not null)
{
    Environment.SetEnvironmentVariable("LLAMAVOZ_MODEL", null);
    Console.WriteLine("Aviso: LLAMAVOZ_MODEL ignorada durante los evals.\n");
}

var cases = TtsCaseGenerator.LoadAndEnsure();
var sharedService = new Lazy<TranscriptionService>(() => new TranscriptionService());

var evalCases = new List<EvalCase>();
foreach (var category in requested)
{
    switch (category)
    {
        case "unit":
            evalCases.AddRange(UnitTestEvals.All());
            break;
        case "asr":
            evalCases.AddRange(AsrEvals.Correctness(sharedService, cases));
            break;
        case "perf":
            evalCases.AddRange(AsrEvals.Performance(sharedService, cases));
            break;
        case "streaming":
            evalCases.AddRange(StreamingEvals.All(sharedService, cases));
            break;
        case "insertion":
            evalCases.AddRange(InsertionEvals.All());
            break;
        case "pipeline":
            evalCases.AddRange(PipelineEvals.All(sharedService, cases));
            break;
        default:
            Console.WriteLine($"Categoría desconocida: {category} (válidas: unit, asr, perf, streaming, insertion, pipeline)");
            return 2;
    }
}

var timer = Stopwatch.StartNew();
var outcomes = await EvalRunner.RunAll(evalCases);
timer.Stop();

var modelsNote = "Modelos: " + string.Join(", ",
    Directory.Exists(RepoPaths.ModelsDir)
        ? Directory.GetFiles(RepoPaths.ModelsDir, "ggml-*.bin").Select(Path.GetFileName)!
        : new[] { "(ninguno)" });
ReportWriter.Write(outcomes, timer.Elapsed, modelsNote);

if (sharedService.IsValueCreated)
{
    sharedService.Value.Dispose();
}

return outcomes.Any(o => o.Status == EvalStatus.Fail) ? 1 : 0;
