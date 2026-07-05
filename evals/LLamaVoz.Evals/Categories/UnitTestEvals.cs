using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace LLamaVoz.Evals.Categories;

/// <summary>Runs the xUnit suite via `dotnet test` and folds the counts into the report.</summary>
public static class UnitTestEvals
{
    public static IEnumerable<EvalCase> All()
    {
        yield return new EvalCase("unit-suite", "unit",
            "Suite xUnit completa (settings, hotkeys, stats, filtros anti-alucinación, WER)",
            () => Task.FromResult(RunDotnetTest()));
    }

    private static CaseResult RunDotnetTest()
    {
        var dotnet = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "dotnet", "dotnet.exe");
        if (!File.Exists(dotnet))
        {
            dotnet = "dotnet"; // PATH fallback
        }

        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            // UseAppHost=false: the tests need App.dll, not App.exe — skipping the apphost
            // avoids a copy conflict with the production app running in parallel.
            Arguments = "test tests/LLamaVoz.Core.Tests -v minimal --nologo -p:UseAppHost=false",
            WorkingDirectory = RepoPaths.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(300_000);

        // VSTest summary, English or Spanish locale.
        var failed = Regex.Match(output, @"(?:Failed|Con error):\s*(\d+)");
        var passed = Regex.Match(output, @"(?:Passed|Superado):\s*(\d+)");
        var metrics = new Dictionary<string, string>
        {
            ["superados"] = passed.Success ? passed.Groups[1].Value : "?",
            ["fallidos"] = failed.Success ? failed.Groups[1].Value : "?",
        };

        if (process.ExitCode == 0 && failed.Success && failed.Groups[1].Value == "0")
        {
            return CaseResult.Pass(null, metrics);
        }

        var tail = string.Join("\n", output.Split('\n').TakeLast(15));
        return CaseResult.Fail($"dotnet test terminó con código {process.ExitCode}.\n```\n{tail}\n```", metrics);
    }
}
