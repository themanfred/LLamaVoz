using System.Runtime.InteropServices;
using System.Text;

namespace LLamaVoz.Insertion.Native;

internal static class NativeMethods
{
    internal const int INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_UNICODE = 0x0004;
    internal const ushort VK_CONTROL = 0x11;
    internal const ushort VK_V = 0x56;
    internal const ushort VK_RETURN = 0x0D;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    internal static string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(512);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public int type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf<INPUT>();
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    internal static INPUT KeyDown(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk } },
    };

    internal static INPUT KeyUp(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } },
    };

    internal static INPUT UnicodeDown(char codeUnit) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wScan = codeUnit, dwFlags = KEYEVENTF_UNICODE } },
    };

    internal static INPUT UnicodeUp(char codeUnit) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wScan = codeUnit, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } },
    };
}
