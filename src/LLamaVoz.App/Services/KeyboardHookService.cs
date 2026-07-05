using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LLamaVoz.DesktopApp.Services;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Low-level keyboard hook for the two activation modes (FR-005/006):
///  - Push-to-talk: hold the PTT modifier combo, release to finish (default Ctrl+Alt).
///  - Toggle: tap the toggle combo (press and release with no other key) to start,
///    tap again to stop (default Win+Alt). Both combos are user-configurable.
/// Uses WH_KEYBOARD_LL because RegisterHotKey cannot observe key release.
/// Injected input (our own SendInput during insertion) is ignored via LLKHF_INJECTED.
/// Must be created on a thread with a message pump (the WPF UI thread).
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;

    private const uint VK_LCONTROL = 0xA2;
    private const uint VK_RCONTROL = 0xA3;
    private const uint VK_LMENU = 0xA4;
    private const uint VK_RMENU = 0xA5;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;
    private const uint VK_ESCAPE = 0x1B;

    private const uint VK_LSHIFT = 0xA0;
    private const uint VK_RSHIFT = 0xA1;

    /// <summary>The configured PTT combo went down (push-to-talk start).</summary>
    public event Action? PttPressed;

    /// <summary>A PTT combo key released after a PTT start.</summary>
    public event Action? PttReleased;

    /// <summary>The toggle combo was pressed and released without any other key (toggle tap).</summary>
    public event Action? ToggleTapped;

    /// <summary>Hold-to-talk modifier combo. Changing it takes effect immediately.</summary>
    public HotkeyModifiers PttCombo { get; set; } = HotkeyModifiers.Ctrl | HotkeyModifiers.Alt;

    /// <summary>Tap-to-toggle modifier combo. Changing it takes effect immediately.</summary>
    public HotkeyModifiers ToggleCombo { get; set; } = HotkeyModifiers.Win | HotkeyModifiers.Alt;

    /// <summary>Parses a settings code like "ctrl+alt" into flags.</summary>
    public static HotkeyModifiers ParseHotkey(string code) =>
        code.Split('+').Aggregate(HotkeyModifiers.None, (acc, part) => acc | part.Trim().ToLowerInvariant() switch
        {
            "ctrl" => HotkeyModifiers.Ctrl,
            "alt" => HotkeyModifiers.Alt,
            "shift" => HotkeyModifiers.Shift,
            "win" => HotkeyModifiers.Win,
            _ => HotkeyModifiers.None,
        });

    /// <summary>Another key pressed while holding Ctrl+Alt (it was a shortcut, not dictation).</summary>
    public event Action? PttInterrupted;

    /// <summary>Escape pressed (controller cancels if a session is active).</summary>
    public event Action? EscapePressed;

    private readonly LowLevelKeyboardProc _proc;
    private readonly IntPtr _hookId;
    private HotkeyModifiers _down;
    private bool _pttActive;
    private bool _togglePending;

    public KeyboardHookService()
    {
        _proc = HookCallback;
        using var module = Process.GetCurrentProcess().MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
        if (_hookId == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"No se pudo instalar el hook de teclado (error {Marshal.GetLastWin32Error()}).");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((info.flags & LLKHF_INJECTED) != 0)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        var msg = wParam.ToInt64();
        var isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        var isUp = msg is WM_KEYUP or WM_SYSKEYUP;

        switch (info.vkCode)
        {
            case VK_LCONTROL or VK_RCONTROL:
                OnModifier(HotkeyModifiers.Ctrl, isDown, isUp);
                break;

            case VK_LMENU or VK_RMENU:
                OnModifier(HotkeyModifiers.Alt, isDown, isUp);
                break;

            case VK_LSHIFT or VK_RSHIFT:
                OnModifier(HotkeyModifiers.Shift, isDown, isUp);
                break;

            case VK_LWIN or VK_RWIN:
                OnModifier(HotkeyModifiers.Win, isDown, isUp);
                break;

            case VK_ESCAPE when isDown:
                _togglePending = false;
                EscapePressed?.Invoke();
                break;

            default:
                if (isDown)
                {
                    // Any other key means the modifiers were part of a shortcut.
                    _togglePending = false;
                    if (_pttActive)
                    {
                        _pttActive = false;
                        PttInterrupted?.Invoke();
                    }
                }
                break;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void OnModifier(HotkeyModifiers modifier, bool isDown, bool isUp)
    {
        if (isDown)
        {
            _down |= modifier;
            // Exact match required: an extra held modifier means a different shortcut.
            if (!_pttActive && _down == PttCombo)
            {
                _pttActive = true;
                PttPressed?.Invoke();
            }
            if (!_pttActive && _down == ToggleCombo)
            {
                _togglePending = true;
            }
        }
        if (isUp)
        {
            _down &= ~modifier;
            if (_pttActive && (_down & PttCombo) != PttCombo)
            {
                _pttActive = false;
                PttReleased?.Invoke();
            }
            if (_togglePending && (_down & ToggleCombo) == HotkeyModifiers.None)
            {
                _togglePending = false;
                ToggleTapped?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
