using System.IO;
using System.Text;

namespace LLamaVoz.Evals;

/// <summary>Renders the console summary and EVALS-REPORT.md at the repo root.</summary>
public static class ReportWriter
{
    public static void Write(List<EvalOutcome> outcomes, TimeSpan totalDuration, string modelsNote)
    {
        var report = Build(outcomes, totalDuration, modelsNote);
        File.WriteAllText(RepoPaths.ReportPath, report, Encoding.UTF8);

        var failures = outcomes.Count(o => o.Status == EvalStatus.Fail);
        var skips = outcomes.Count(o => o.Status == EvalStatus.Skip);
        var passes = outcomes.Count(o => o.Status == EvalStatus.Pass);
        Console.WriteLine();
        Console.WriteLine($"RESULTADO: {passes} PASS · {failures} FAIL · {skips} SKIP  →  " +
                          (failures == 0 ? "SUITE OK" : "SUITE CON FALLOS"));
        Console.WriteLine($"Reporte: {RepoPaths.ReportPath}");
    }

    private static string Build(List<EvalOutcome> outcomes, TimeSpan totalDuration, string modelsNote)
    {
        var sb = new StringBuilder();
        var failures = outcomes.Where(o => o.Status == EvalStatus.Fail).ToList();
        var skips = outcomes.Where(o => o.Status == EvalStatus.Skip).ToList();

        sb.AppendLine("# LLamaVoz — Reporte de Evals");
        sb.AppendLine();
        sb.AppendLine($"Generado: {DateTime.Now:yyyy-MM-dd HH:mm} · Máquina: {Environment.MachineName}, " +
                      $"{Environment.ProcessorCount} núcleos lógicos · {modelsNote}");
        sb.AppendLine();
        sb.AppendLine("> **Nota de honestidad:** el audio de prueba es TTS sintético de Windows (voces Sabina/Zira). " +
                      "Las cifras de WER reflejan ese audio, no voz humana real de campo cercano. " +
                      "Los umbrales son hipótesis iniciales del PRD §31, ajustables con datos de beta.");
        sb.AppendLine();

        // Summary per category
        sb.AppendLine("## Resumen");
        sb.AppendLine();
        sb.AppendLine("| Categoría | Pass | Fail | Skip | Total |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var group in outcomes.GroupBy(o => o.Category))
        {
            sb.AppendLine($"| {group.Key} | {group.Count(o => o.Status == EvalStatus.Pass)} | " +
                          $"{group.Count(o => o.Status == EvalStatus.Fail)} | " +
                          $"{group.Count(o => o.Status == EvalStatus.Skip)} | {group.Count()} |");
        }
        sb.AppendLine($"| **total** | **{outcomes.Count(o => o.Status == EvalStatus.Pass)}** | " +
                      $"**{failures.Count}** | **{skips.Count}** | **{outcomes.Count}** |");
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0
            ? "**RESULTADO GLOBAL: ✅ PASS**"
            : $"**RESULTADO GLOBAL: ❌ FAIL ({failures.Count} caso(s) fallando)**");
        sb.AppendLine($"\nDuración total: {totalDuration.TotalMinutes:F1} min");
        sb.AppendLine();

        // Failures first — that's what the user asked to see
        if (failures.Count > 0)
        {
            sb.AppendLine("## ❌ Dónde falla");
            sb.AppendLine();
            foreach (var f in failures)
            {
                sb.AppendLine($"### {f.Id} — FAIL");
                sb.AppendLine($"- **Qué evalúa:** {f.Description}");
                sb.AppendLine($"- **Por qué falló:** {f.Detail}");
                foreach (var (key, value) in f.Metrics)
                {
                    sb.AppendLine($"- {key}: {value}");
                }
                sb.AppendLine();
            }
        }

        // Detail tables per category
        foreach (var group in outcomes.GroupBy(o => o.Category))
        {
            sb.AppendLine($"## Detalle — {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Caso | Estado | Métricas | Duración |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var o in group)
            {
                var metrics = o.Metrics.Count == 0
                    ? "—"
                    : string.Join("<br>", o.Metrics.Select(m => $"{m.Key}: {m.Value}"));
                sb.AppendLine($"| {o.Id} | {StatusIcon(o.Status)} | {metrics} | {o.Duration.TotalSeconds:F1}s |");
            }
            sb.AppendLine();
        }

        if (skips.Count > 0)
        {
            sb.AppendLine("## ○ Omitidos");
            sb.AppendLine();
            sb.AppendLine("| Caso | Motivo |");
            sb.AppendLine("|---|---|");
            foreach (var s in skips)
            {
                sb.AppendLine($"| {s.Id} | {s.Detail} |");
            }
            sb.AppendLine();
            sb.AppendLine("_Los casos omitidos requieren el escritorio libre: vuelve a ejecutar `RunEvals.cmd` sin tocar teclado/ratón durante la fase de inserción._");
        }

        return sb.ToString();
    }

    private static string StatusIcon(EvalStatus status) => status switch
    {
        EvalStatus.Pass => "✅ PASS",
        EvalStatus.Fail => "❌ FAIL",
        _ => "○ SKIP",
    };
}
