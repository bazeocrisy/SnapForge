using System.Runtime.InteropServices;

namespace SnapForge.App.Tests;

/// <summary>
/// Real mouse input through the Windows input stack (SendInput), so tests exercise the same code paths a
/// user does: hit testing, mouse capture, LostMouseCapture ordering, button state.
/// Requires an unlocked interactive desktop (GitHub-hosted Windows runners provide one).
/// </summary>
internal static unsafe partial class NativeInput
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    public static (int X, int Y) CursorPosition => GetCursorPos(out POINT p) ? (p.X, p.Y) : (int.MinValue, int.MinValue);

    /// <summary>Moves the cursor to an exact physical position and emits a mouse-move event there.</summary>
    public static void MoveTo(int x, int y)
    {
        if (!SetCursorPos(x, y))
        {
            throw new InvalidOperationException($"SetCursorPos({x},{y}) failed (error {Marshal.GetLastPInvokeError()}); is the desktop interactive?");
        }

        Send(MOUSEEVENTF_MOVE); // zero-distance move: guarantees WM_MOUSEMOVE at the new position
    }

    public static void LeftDown() => Send(MOUSEEVENTF_LEFTDOWN);

    public static void LeftUp() => Send(MOUSEEVENTF_LEFTUP);

    public static void RightClick()
    {
        Send(MOUSEEVENTF_RIGHTDOWN);
        Send(MOUSEEVENTF_RIGHTUP);
    }

    /// <summary>Presses and releases a virtual key (e.g. 0x1B = Esc). Works without the foreground window for OLE drag loops, which poll key state.</summary>
    public static void KeyPress(ushort virtualKey)
    {
        var inputs = stackalloc INPUT[2];
        inputs[0] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = virtualKey } };
        inputs[1] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = KEYEVENTF_KEYUP } };
        _ = SendInput(2, inputs, sizeof(INPUT));
    }

    public static void TryEnablePerMonitorDpiAwareness()
    {
        // Must match the app manifest so physical coordinates agree; ignored if already set for the process.
        _ = SetProcessDpiAwarenessContext(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    }

    private static void Send(uint flags)
    {
        var input = new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = flags } };
        if (SendInput(1, &input, sizeof(INPUT)) != 1)
        {
            throw new InvalidOperationException($"SendInput failed (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)]
        public uint type;

        // Union with KEYBDINPUT/HARDWAREINPUT; MOUSEINPUT is the largest member.
        [FieldOffset(8)]
        public MOUSEINPUT mi;

        [FieldOffset(8)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    /// <summary>
    /// Describes the top-level window Windows finds at a screen point (what OLE uses to pick the drop target):
    /// class name, title, owning process id and the window's own handle.
    /// </summary>
    public static string WindowAt(int x, int y)
    {
        nint h = WindowFromPoint(new POINT { X = x, Y = y });
        if (h == 0)
        {
            return "none";
        }

        nint root = GetAncestor(h, 2 /* GA_ROOT */);
        char* cls = stackalloc char[128];
        char* title = stackalloc char[128];
        int nc = GetClassNameW(root, cls, 128);
        int nt = GetWindowTextW(root, title, 128);
        _ = GetWindowThreadProcessId(root, out uint pid);
        return $"0x{root:X} class '{new string(cls, 0, nc)}' title '{new string(title, 0, nt)}' pid {pid}";
    }

    /// <summary>Root window handle at a screen point.</summary>
    public static nint RootWindowAt(int x, int y) => GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2);

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(POINT p);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int GetClassNameW(nint hwnd, char* name, int max);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextW(nint hwnd, char* text, int max);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT p);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint cInputs, INPUT* pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint value);
}
