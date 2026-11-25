using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using SnapForge.Core.Settings;

namespace SnapForge.App.Interop;

/// <summary>Outcome of registering one global hotkey.</summary>
internal sealed record HotkeyRegistration(string Action, HotkeyGesture Gesture, bool Succeeded, string? Error);

/// <summary>
/// Hidden message-only window that owns SnapForge's notification-area icon and global hotkeys.
/// Hotkeys use RegisterHotKey: if another program (e.g. Windows' Print Screen → Snipping Tool setting) already owns
/// a combination, registration fails and the conflict is reported instead of silently not working.
/// </summary>
internal sealed unsafe partial class ShellIntegration : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_APP_TRAY = 0x8000 + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const uint NIIF_INFO = 0x1, NIIF_WARNING = 0x2;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _hotkeyActions = [];
    private readonly uint _taskbarCreated;
    private nint _icon;
    private bool _iconAdded;
    private int _nextHotkeyId = 1;

    public ShellIntegration()
    {
        _source = new HwndSource(new HwndSourceParameters("SnapForgeShell") { ParentWindow = -3 /* HWND_MESSAGE */, WindowStyle = 0 });
        _source.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    }

    /// <summary>Raised on left click / double click of the notification-area icon.</summary>
    public event EventHandler? IconActivated;

    /// <summary>Raised on right click (or keyboard context menu) of the notification-area icon.</summary>
    public event EventHandler? IconContextMenuRequested;

    public nint Handle => _source.Handle;

    public void ShowIcon(string tooltip)
    {
        if (_icon == 0)
        {
            nint large = 0, small = 0;
            if (ExtractIconExW(Environment.ProcessPath ?? string.Empty, 0, &large, &small, 1) > 0)
            {
                _icon = small != 0 ? small : large;
                if (large != 0 && large != _icon)
                {
                    DestroyIcon(large);
                }
            }
        }

        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        SetString(data.szTip, 128, tooltip);
        _iconAdded = Shell_NotifyIconW(_iconAdded ? NIM_MODIFY : NIM_ADD, &data);
        if (_iconAdded)
        {
            data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIconW(NIM_SETVERSION, &data);
        }
    }

    /// <summary>Shows a standard Windows notification from the icon (no network, no data leaves the PC).</summary>
    public void ShowBalloon(string title, string text, bool warning = false)
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = NewData(NIF_INFO);
        SetString(data.szInfoTitle, 64, title);
        SetString(data.szInfo, 256, text);
        data.dwInfoFlags = warning ? NIIF_WARNING : NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    public void HideIcon()
    {
        if (_iconAdded)
        {
            var data = NewData(0);
            Shell_NotifyIconW(NIM_DELETE, &data);
            _iconAdded = false;
        }
    }

    /// <summary>Unregisters all hotkeys, then registers the given ones. Returns one result per hotkey.</summary>
    public IReadOnlyList<HotkeyRegistration> RegisterHotkeys(IEnumerable<(string Action, HotkeyGesture? Gesture, Action Handler)> hotkeys)
    {
        UnregisterAllHotkeys();
        var results = new List<HotkeyRegistration>();
        foreach ((string action, HotkeyGesture? gesture, Action handler) in hotkeys)
        {
            if (gesture is null)
            {
                continue;
            }

            if (!gesture.IsValid)
            {
                results.Add(new HotkeyRegistration(action, gesture, false, gesture.Problem));
                continue;
            }

            if (!Enum.TryParse(gesture.NormalizedKey, ignoreCase: true, out Key key) || KeyInterop.VirtualKeyFromKey(key) is not (> 0 and var vk))
            {
                results.Add(new HotkeyRegistration(action, gesture, false, $"Unknown key '{gesture.Key}'."));
                continue;
            }

            if (results.Any(r => r.Succeeded && r.Gesture.SameKeysAs(gesture)))
            {
                results.Add(new HotkeyRegistration(action, gesture, false, "Already used by another SnapForge action."));
                continue;
            }

            int id = _nextHotkeyId++;
            if (RegisterHotKey(_source.Handle, id, (uint)gesture.Modifiers | MOD_NOREPEAT, (uint)vk))
            {
                _hotkeyActions[id] = handler;
                results.Add(new HotkeyRegistration(action, gesture, true, null));
            }
            else
            {
                int error = Marshal.GetLastPInvokeError();
                string message = error == ERROR_HOTKEY_ALREADY_REGISTERED
                    ? "In use by another program or by Windows."
                    : new Win32Exception(error).Message;
                results.Add(new HotkeyRegistration(action, gesture, false, message));
            }
        }

        return results;
    }

    public void UnregisterAllHotkeys()
    {
        foreach (int id in _hotkeyActions.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _hotkeyActions.Clear();
    }

    public void Dispose()
    {
        UnregisterAllHotkeys();
        HideIcon();
        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _hotkeyActions.TryGetValue((int)wParam, out Action? action))
        {
            handled = true;
            action();
        }
        else if (msg == WM_APP_TRAY)
        {
            int evt = (int)(lParam & 0xFFFF); // NOTIFYICON_VERSION_4: LOWORD(lParam) is the event
            if (evt is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                IconActivated?.Invoke(this, EventArgs.Empty);
            }
            else if (evt is WM_RBUTTONUP or WM_CONTEXTMENU)
            {
                // Required so the menu closes when the user clicks elsewhere.
                SetForegroundWindow(_source.Handle);
                IconContextMenuRequested?.Invoke(this, EventArgs.Empty);
            }

            handled = true;
        }
        else if (_taskbarCreated != 0 && msg == (int)_taskbarCreated && _iconAdded)
        {
            // Explorer restarted: the icon must be added again.
            _iconAdded = false;
            ShowIcon(ProductInfoTooltip);
        }

        return 0;
    }

    private const string ProductInfoTooltip = "SnapForge";

    private NOTIFYICONDATAW NewData(uint flags) => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = _source.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
    };

    private static void SetString(char* target, int capacity, string value)
    {
        int n = Math.Min(value.Length, capacity - 1);
        for (int i = 0; i < n; i++)
        {
            target[i] = value[i];
        }

        target[n] = '\0';
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint dwMessage, NOTIFYICONDATAW* lpData);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconExW(string lpszFile, int nIconIndex, nint* phiconLarge, nint* phiconSmall, uint nIcons);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);
}
