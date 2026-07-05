using System.Runtime.InteropServices;
using LLamaVoz.Core.Insertion;
using LLamaVoz.Insertion;

namespace LLamaVoz.Evals.Categories;

/// <summary>
/// Insertion engine evals against a self-owned test window (never user apps).
/// Cases needing foreground focus SKIP when the desktop is in use — Windows
/// (correctly) refuses focus stealing, and the engine's FR-028 guard would block.
/// </summary>
public static class InsertionEvals
{
    private const string TestText = "Hola, LLamaVoz 🦙 — áéíóú ñ ¿listo?";
    private const string ClipboardSentinel = "SENTINEL-portapapeles-evals";

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public static IEnumerable<EvalCase> All()
    {
        yield return new EvalCase("ins-uia-tier1", "insertion",
            "Tier 1 — UIA ValuePattern en control vacío", () => Task.FromResult(
                RunTierScenario(new InsertionOptions { AllowKeyboardInput = false, AllowClipboard = false })));

        yield return new EvalCase("ins-keyboard-tier2", "insertion",
            "Tier 2 — SendInput Unicode (acentos + emoji, par sustituto)", () => Task.FromResult(
                RunTierScenario(new InsertionOptions { AllowUia = false, AllowClipboard = false })));

        yield return new EvalCase("ins-clipboard-tier3", "insertion",
            "Tier 3 — portapapeles con respaldo y restauración (FR-027)", () => Task.FromResult(
                RunTierScenario(new InsertionOptions { AllowUia = false, AllowKeyboardInput = false },
                    checkClipboardRestore: true)));

        yield return new EvalCase("ins-window-change-block", "insertion",
            "FR-028 — con la ventana objetivo fuera de foco NO se inserta nada", () => Task.FromResult(
                RunWindowChangeScenario()));

        yield return new EvalCase("ins-password-block", "insertion",
            "FR-030 — un campo de contraseña bloquea la inserción", () => Task.FromResult(
                RunPasswordScenario()));
    }

    private static CaseResult RunTierScenario(InsertionOptions options, bool checkClipboardRestore = false)
    {
        if (checkClipboardRestore)
        {
            StaRun(() => System.Windows.Forms.Clipboard.SetDataObject(ClipboardSentinel, true, 5, 50));
        }

        using var host = TestWindowHost.Start();
        if (!host.WaitReady(TimeSpan.FromSeconds(5)))
        {
            return CaseResult.Fail("La ventana de prueba no arrancó.");
        }

        if (!TryFocus(host.WindowHandle))
        {
            return CaseResult.Skip("Escritorio en uso: la ventana de prueba no pudo tomar el foco.");
        }

        using var engine = new InsertionEngine();
        var result = engine.Insert(TestText, TargetWindowGuard.ForWindow(host.WindowHandle), options);
        Thread.Sleep(600); // typed input flows through the message queue
        var readBack = host.GetTextBoxText();

        var metrics = new Dictionary<string, string>
        {
            ["método"] = result.MethodUsed.ToString(),
            ["leído"] = $"\"{readBack}\"",
        };

        if (!result.Success)
        {
            return CaseResult.Fail($"Inserción falló: {result.Detail}", metrics);
        }
        if (readBack != TestText)
        {
            return CaseResult.Fail($"Texto leído no coincide (esperado \"{TestText}\").", metrics);
        }

        if (checkClipboardRestore)
        {
            string restored = "";
            StaRun(() => restored = System.Windows.Forms.Clipboard.ContainsText()
                ? System.Windows.Forms.Clipboard.GetText()
                : "(vacío)");
            metrics["portapapeles tras restaurar"] = $"\"{restored}\"";
            if (restored != ClipboardSentinel)
            {
                return CaseResult.Fail("El contenido previo del portapapeles NO se restauró (FR-027).", metrics);
            }
        }

        return CaseResult.Pass(null, metrics);
    }

    private static CaseResult RunWindowChangeScenario()
    {
        using var hostA = TestWindowHost.Start();
        using var hostB = TestWindowHost.Start();
        if (!hostA.WaitReady(TimeSpan.FromSeconds(5)) || !hostB.WaitReady(TimeSpan.FromSeconds(5)))
        {
            return CaseResult.Fail("Las ventanas de prueba no arrancaron.");
        }

        // Target = A, focus = B (or whatever the user has focused — equally valid for this test).
        SetForegroundWindow(hostB.WindowHandle);
        Thread.Sleep(300);

        using var engine = new InsertionEngine();
        var result = engine.Insert(TestText, TargetWindowGuard.ForWindow(hostA.WindowHandle));

        var blocked = !result.Success && result.FailureReason == InsertionFailureReason.TargetWindowChanged;
        var nothingTyped = hostA.GetTextBoxText().Length == 0;
        var metrics = new Dictionary<string, string>
        {
            ["bloqueado"] = blocked.ToString(),
            ["texto en ventana objetivo"] = nothingTyped ? "(nada)" : "¡SE INSERTÓ!",
        };
        return blocked && nothingTyped
            ? CaseResult.Pass(null, metrics)
            : CaseResult.Fail("El motor no bloqueó la inserción con la ventana cambiada.", metrics);
    }

    private static CaseResult RunPasswordScenario()
    {
        using var host = TestWindowHost.Start(passwordBox: true);
        if (!host.WaitReady(TimeSpan.FromSeconds(5)))
        {
            return CaseResult.Fail("La ventana de prueba no arrancó.");
        }
        if (!TryFocus(host.WindowHandle))
        {
            return CaseResult.Skip("Escritorio en uso: la ventana de prueba no pudo tomar el foco.");
        }

        using var engine = new InsertionEngine();
        var result = engine.Insert(TestText, TargetWindowGuard.ForWindow(host.WindowHandle));
        var metrics = new Dictionary<string, string>
        {
            ["motivo"] = result.FailureReason.ToString(),
            ["texto en el campo"] = host.GetTextBoxText().Length == 0 ? "(nada)" : "¡SE INSERTÓ!",
        };
        return !result.Success && result.FailureReason == InsertionFailureReason.SensitiveFieldBlocked
            ? CaseResult.Pass(null, metrics)
            : CaseResult.Fail("El campo de contraseña NO bloqueó la inserción (FR-030).", metrics);
    }

    internal static bool TryFocus(IntPtr handle)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            SetForegroundWindow(handle);
            Thread.Sleep(250);
            if (GetForegroundWindow() == handle)
            {
                return true;
            }
        }
        return false;
    }

    private static void StaRun(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }
        var thread = new Thread(() => action());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
