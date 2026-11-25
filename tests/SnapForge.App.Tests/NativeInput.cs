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
    }

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
