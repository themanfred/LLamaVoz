using LLamaVoz.Insertion.Native;

namespace LLamaVoz.Insertion;

/// <summary>
/// Captures the foreground window when dictation starts and verifies it is still the
/// foreground window right before inserting (FR-028: never insert into the wrong window).
/// </summary>
public sealed class TargetWindowGuard
{
    public IntPtr WindowHandle { get; }
    public string WindowTitle { get; }
    public uint ProcessId { get; }

    private TargetWindowGuard(IntPtr handle, string title, uint processId)
    {
        WindowHandle = handle;
        WindowTitle = title;
        ProcessId = processId;
    }

    public static TargetWindowGuard CaptureForeground()
    {
        var handle = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(handle, out var pid);
        return new TargetWindowGuard(handle, NativeMethods.GetWindowTitle(handle), pid);
    }

    /// <summary>Wrap an explicit window handle (used by tests and the POC harness).</summary>
    public static TargetWindowGuard ForWindow(IntPtr handle)
    {
        NativeMethods.GetWindowThreadProcessId(handle, out var pid);
        return new TargetWindowGuard(handle, NativeMethods.GetWindowTitle(handle), pid);
    }

    public bool IsStillForeground(out string currentTitle)
    {
        var current = NativeMethods.GetForegroundWindow();
        currentTitle = NativeMethods.GetWindowTitle(current);
        return current == WindowHandle;
    }
}
