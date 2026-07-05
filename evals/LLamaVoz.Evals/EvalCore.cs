using System.Diagnostics;
using System.IO;

namespace LLamaVoz.Evals;

public enum EvalStatus { Pass, Fail, Skip }

/// <summary>Result of one eval case: status + human detail + metric key/values.</summary>
public sealed record CaseResult(EvalStatus Status, string? Detail = null, Dictionary<string, string>? Metrics = null)
{
    public static CaseResult Pass(string? detail = null, Dictionary<string, string>? metrics = null) =>
        new(EvalStatus.Pass, detail, metrics);

    public static CaseResult Fail(string detail, Dictionary<string, string>? metrics = null) =>
        new(EvalStatus.Fail, detail, metrics);

    public static CaseResult Skip(string reason) => new(EvalStatus.Skip, reason);
}

public sealed record EvalOutcome(
    string Id,
    string Category,
    string Description,
    EvalStatus Status,
    string? Detail,
    IReadOnlyDictionary<string, string> Metrics,
    TimeSpan Duration);

public sealed record EvalCase(string Id, string Category, string Description, Func<Task<CaseResult>> Run);

public static class EvalRunner
{
    public static async Task<List<EvalOutcome>> RunAll(IEnumerable<EvalCase> cases)
    {
        var outcomes = new List<EvalOutcome>();
        foreach (var evalCase in cases)
        {
            Console.Write($"  [{evalCase.Category}] {evalCase.Id} ... ");
            var timer = Stopwatch.StartNew();
            CaseResult result;
            try
            {
                result = await evalCase.Run();
            }
            catch (Exception ex)
            {
                result = CaseResult.Fail($"Excepción no controlada: {ex.GetType().Name}: {ex.Message}");
            }
            timer.Stop();

            Console.WriteLine($"{StatusLabel(result.Status)} ({timer.Elapsed.TotalSeconds:F1}s)" +
                              (result.Status != EvalStatus.Pass && result.Detail is not null ? $" — {result.Detail}" : ""));
            outcomes.Add(new EvalOutcome(
                evalCase.Id, evalCase.Category, evalCase.Description,
                result.Status, result.Detail,
                result.Metrics ?? new Dictionary<string, string>(),
                timer.Elapsed));
        }
        return outcomes;
    }

    public static string StatusLabel(EvalStatus status) => status switch
    {
        EvalStatus.Pass => "PASS ✔",
        EvalStatus.Fail => "FAIL ✘",
        _ => "SKIP ○",
    };
}

/// <summary>Locates repo folders regardless of where the eval binary runs from.</summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string ModelsDir => Path.Combine(Root, "models");
    public static string PocAudioDir => Path.Combine(Root, "poc", "audio");
    public static string EvalAudioDir => Path.Combine(Root, "evals", "audio");
    public static string ReportPath => Path.Combine(Root, "EVALS-REPORT.md");
    public static string CasesPath => Path.Combine(Root, "evals", "cases.json");
    public static string TestsProject => Path.Combine(Root, "tests", "LLamaVoz.Core.Tests");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent!)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LLamaVoz.sln")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("No se encontró LLamaVoz.sln hacia arriba del binario de evals.");
    }
}
