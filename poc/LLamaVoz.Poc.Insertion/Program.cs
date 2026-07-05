using System.Runtime.InteropServices;
using LLamaVoz.Core.Insertion;
using LLamaVoz.Insertion;

namespace LLamaVoz.Poc.Insertion;

/// <summary>
/// POC-1 harness (IMPLEMENTATION_PLAN.md M1).
/// Modes:
///   selftest              — automated: spawns its own window + TextBox and verifies each tier.
///   countdown [s] [text]  — manual: gives you [s] seconds to focus any field, then inserts.
///   inspect [s]           — prints UIA info about the focused control for [s] seconds.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "selftest";
        return mode switch
        {
            "selftest" => SelfTest.Run(),
            "countdown" => Countdown(args),
            "inspect" => Inspect(args),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.WriteLine("Usage: LLamaVoz.Poc.Insertion [selftest|countdown [seconds] [text]|inspect [seconds]]");
        return 2;
    }

    private static int Countdown(string[] args)
    {
        var seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 5;
        var text = args.Length > 2
            ? string.Join(' ', args.Skip(2))
            : "Hola desde LLamaVoz 🦙 — prueba de inserción con áéíóú y ñ.";

        Console.WriteLine($"Enfoca el campo de destino. Insertando en {seconds} segundos...");
        for (var i = seconds; i > 0; i--)
        {
            Console.Write($"\r{i}... ");
            Thread.Sleep(1000);
        }
        Console.WriteLine();

        var guard = TargetWindowGuard.CaptureForeground();
        Console.WriteLine($"Ventana objetivo: '{guard.WindowTitle}' (PID {guard.ProcessId})");

        using var engine = new InsertionEngine();
        var result = engine.Insert(text, guard);
        PrintResult(result);
        return result.Success ? 0 : 1;
    }

    private static int Inspect(string[] args)
    {
        var seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 10;
        using var automation = new FlaUI.UIA3.UIA3Automation();

        Console.WriteLine($"Inspeccionando el elemento con foco durante {seconds} segundos (Ctrl+C para salir)...");
        for (var i = 0; i < seconds; i++)
        {
            try
            {
                var el = automation.FocusedElement();
                var patterns = new List<string>();
                if (el.Patterns.Value.IsSupported) patterns.Add("ValuePattern");
                if (el.Patterns.Text.IsSupported) patterns.Add("TextPattern");
                if (el.Patterns.LegacyIAccessible.IsSupported) patterns.Add("LegacyIAccessible");

                Console.WriteLine(
                    $"[{i + 1,2}s] {el.Properties.ControlType.ValueOrDefault} " +
                    $"'{el.Properties.Name.ValueOrDefault}' " +
                    $"password={el.Properties.IsPassword.ValueOrDefault} " +
                    $"patterns=[{string.Join(", ", patterns)}]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{i + 1,2}s] (no se pudo leer el foco: {ex.Message})");
            }
            Thread.Sleep(1000);
        }
        return 0;
    }

    internal static void PrintResult(InsertionResult result)
    {
        Console.WriteLine($"  Éxito:    {result.Success}");
        Console.WriteLine($"  Método:   {result.MethodUsed}");
        if (result.FailureReason != InsertionFailureReason.None)
        {
            Console.WriteLine($"  Motivo:   {result.FailureReason}");
        }
        Console.WriteLine($"  Detalle:  {result.Detail}");
        foreach (var w in result.Warnings)
        {
            Console.WriteLine($"  Aviso:    {w}");
        }
    }
}

/// <summary>
/// Automated smoke test. Spawns a WinForms window owned by this process so nothing is ever
/// typed into a user application. The engine's own FR-028 guard is pointed at the test window,
/// so if focus is stolen mid-test the insertion is blocked instead of going astray.
/// </summary>
internal static class SelfTest
{
    private const string TestText = "Hola, LLamaVoz 🦙 — áéíóú ñ ¿listo?";
    private const string ClipboardSentinel = "SENTINEL-portapapeles-original";

    private enum Outcome { Passed, Failed, Skipped }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public static int Run()
    {
        var scenarios = new (string Name, InsertionOptions Options)[]
        {
            ("Tier 1 — UIA ValuePattern", new InsertionOptions { AllowKeyboardInput = false, AllowClipboard = false }),
            ("Tier 2 — Unicode SendInput", new InsertionOptions { AllowUia = false, AllowClipboard = false }),
            ("Tier 3 — Clipboard paste", new InsertionOptions { AllowUia = false, AllowKeyboardInput = false }),
        };

        var failures = 0;
        var skipped = 0;

        Console.WriteLine("=== UIA headless — SetValue directo (sin requerir foco) ===");
        switch (RunHeadlessUiaScenario())
        {
            case Outcome.Failed: failures++; break;
            case Outcome.Skipped: skipped++; break;
        }

        foreach (var (name, options) in scenarios)
        {
            Console.WriteLine($"\n=== {name} ===");
            switch (RunScenario(options))
            {
                case Outcome.Failed: failures++; break;
                case Outcome.Skipped: skipped++; break;
            }
        }

        Console.WriteLine("\n=== FR-028 — bloqueo por cambio de ventana ===");
        if (!RunWindowChangeScenario())
        {
            failures++;
        }

        Console.WriteLine($"\nRESULTADO: {5 - failures - skipped} pasaron, {failures} fallaron, {skipped} omitidos.");
        if (skipped > 0)
        {
            Console.WriteLine("Los escenarios omitidos necesitan el escritorio libre: ejecuta el selftest y no toques");
            Console.WriteLine("el teclado ni el ratón durante ~15 segundos para que la ventana de prueba tome el foco.");
        }
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Validates the UIA plumbing without needing foreground focus: finds the test TextBox
    /// by handle and sets its value directly. This is the part of Tier 1 that can run while
    /// the user keeps working.
    /// </summary>
    private static Outcome RunHeadlessUiaScenario()
    {
        using var host = TestWindowHost.Start();
        if (!host.WaitReady(TimeSpan.FromSeconds(5)))
        {
            Console.WriteLine("  FALLO: la ventana de prueba no arrancó.");
            return Outcome.Failed;
        }

        try
        {
            using var automation = new FlaUI.UIA3.UIA3Automation();
            var window = automation.FromHandle(host.WindowHandle);
            var edit = window.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
            if (edit is null)
            {
                Console.WriteLine("  FALLO: no se encontró el TextBox vía UIA.");
                return Outcome.Failed;
            }

            edit.Patterns.Value.Pattern.SetValue(TestText);
            Thread.Sleep(200);
            var readBack = host.GetTextBoxText();
            Console.WriteLine($"  Leído:    \"{readBack}\"");

            var ok = readBack == TestText;
            Console.WriteLine(ok ? "  ✔ PASÓ" : "  ✘ FALLÓ");
            return ok ? Outcome.Passed : Outcome.Failed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FALLO: {ex.Message}");
            return Outcome.Failed;
        }
    }

    private static Outcome RunScenario(InsertionOptions options)
    {
        var isClipboardTier = !options.AllowUia && !options.AllowKeyboardInput && options.AllowClipboard;
        if (isClipboardTier)
        {
            System.Windows.Forms.Clipboard.SetDataObject(ClipboardSentinel, copy: true, retryTimes: 5, retryDelay: 50);
        }

        using var host = TestWindowHost.Start();
        if (!host.WaitReady(TimeSpan.FromSeconds(5)))
        {
            Console.WriteLine("  FALLO: la ventana de prueba no arrancó.");
            return Outcome.Failed;
        }

        // Windows refuses focus-stealing while the user is active in another app. Retry
        // briefly; if we never get the foreground, skip rather than fail — the engine's
        // FR-028 guard would (correctly) block the insertion anyway.
        var gotForeground = false;
        for (var attempt = 0; attempt < 8 && !gotForeground; attempt++)
        {
            SetForegroundWindow(host.WindowHandle);
            Thread.Sleep(250);
            gotForeground = GetForegroundWindow() == host.WindowHandle;
        }

        if (!gotForeground)
        {
            Console.WriteLine("  OMITIDO: el escritorio está en uso; la ventana de prueba no pudo tomar el foco.");
            return Outcome.Skipped;
        }

        var guard = TargetWindowGuard.ForWindow(host.WindowHandle);
        using var engine = new InsertionEngine();
        var result = engine.Insert(TestText, guard, options);

        // Typed input flows through the message queue; give it time to land.
        Thread.Sleep(600);
        var readBack = host.GetTextBoxText();

        Program.PrintResult(result);
        Console.WriteLine($"  Leído:    \"{readBack}\"");

        var ok = result.Success && readBack == TestText;

        if (isClipboardTier)
        {
            var restored = System.Windows.Forms.Clipboard.ContainsText()
                ? System.Windows.Forms.Clipboard.GetText()
                : "(vacío)";
            var restoreOk = restored == ClipboardSentinel;
            Console.WriteLine($"  Portapapeles restaurado: {(restoreOk ? "sí" : $"NO (contiene: \"{restored}\")")}");
            ok &= restoreOk;
        }

        Console.WriteLine(ok ? "  ✔ PASÓ" : "  ✘ FALLÓ");
        return ok ? Outcome.Passed : Outcome.Failed;
    }

    /// <summary>The engine must refuse to insert when the foreground window is not the target.</summary>
    private static bool RunWindowChangeScenario()
    {
        using var hostA = TestWindowHost.Start();
        using var hostB = TestWindowHost.Start();
        if (!hostA.WaitReady(TimeSpan.FromSeconds(5)) || !hostB.WaitReady(TimeSpan.FromSeconds(5)))
        {
            Console.WriteLine("  FALLO: las ventanas de prueba no arrancaron.");
            return false;
        }

        // Target is window A, but we deliberately focus window B — simulating the user
        // switching windows while dictation was being processed.
        SetForegroundWindow(hostB.WindowHandle);
        Thread.Sleep(400);

        var guard = TargetWindowGuard.ForWindow(hostA.WindowHandle);
        using var engine = new InsertionEngine();
        var result = engine.Insert(TestText, guard);

        Program.PrintResult(result);

        var blocked = !result.Success && result.FailureReason == InsertionFailureReason.TargetWindowChanged;
        var nothingTyped = hostA.GetTextBoxText().Length == 0 && hostB.GetTextBoxText().Length == 0;

        var ok = blocked && nothingTyped;
        Console.WriteLine($"  Bloqueado correctamente: {blocked}; ningún texto insertado: {nothingTyped}");
        Console.WriteLine(ok ? "  ✔ PASÓ" : "  ✘ FALLÓ");
        return ok;
    }
}

/// <summary>Hosts a WinForms window with a single TextBox on its own STA thread.</summary>
internal sealed class TestWindowHost : IDisposable
{
    private readonly ManualResetEventSlim _ready = new();
    private System.Windows.Forms.Form? _form;
    private System.Windows.Forms.TextBox? _textBox;
    private Thread? _thread;

    public IntPtr WindowHandle { get; private set; }

    public static TestWindowHost Start()
    {
        var host = new TestWindowHost();
        host._thread = new Thread(host.RunMessageLoop) { IsBackground = true };
        host._thread.SetApartmentState(ApartmentState.STA);
        host._thread.Start();
        return host;
    }

    private void RunMessageLoop()
    {
        _form = new System.Windows.Forms.Form
        {
            Text = "LLamaVoz POC — ventana de prueba",
            Width = 520,
            Height = 160,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
        };
        _textBox = new System.Windows.Forms.TextBox
        {
            Multiline = true,
            Dock = System.Windows.Forms.DockStyle.Fill,
        };
        _form.Controls.Add(_textBox);
        _form.Shown += (_, _) =>
        {
            WindowHandle = _form.Handle;
            _textBox.Focus();
            _ready.Set();
        };
        System.Windows.Forms.Application.Run(_form);
    }

    public bool WaitReady(TimeSpan timeout) => _ready.Wait(timeout);

    public string GetTextBoxText()
    {
        if (_form is null || _textBox is null)
        {
            return string.Empty;
        }
        return (string)_form.Invoke(() => _textBox.Text);
    }

    public void Dispose()
    {
        try
        {
            if (_form is not null && _form.IsHandleCreated)
            {
                _form.Invoke(() => _form.Close());
            }
            _thread?.Join(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Best-effort teardown for a test window.
        }
        _ready.Dispose();
    }
}
