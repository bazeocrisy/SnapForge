# QA helper for the ui-audit CI job (test-only): change the display resolution and the Windows display SCALING of the
# primary monitor live, the same way Settings → System → Display → Scale does (DisplayConfigSetDeviceInfo, applied
# without signing out), and read both back so each audit run can be marked verified or unverified.
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class SfDisplay
{
    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] public struct SourceInfo { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint n; public uint d; }
    [StructLayout(LayoutKind.Sequential)] public struct TargetInfo { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling; public Rational refreshRate; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct PathInfo { public SourceInfo source; public TargetInfo target; public uint flags; }
    [StructLayout(LayoutKind.Sequential, Size = 64)] public struct ModeInfo { public uint infoType; }
    [StructLayout(LayoutKind.Sequential)] public struct Header { public int type; public uint size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential)] public struct GetScale { public Header header; public int minRel; public int curRel; public int maxRel; }
    [StructLayout(LayoutKind.Sequential)] public struct SetScale { public Header header; public int scaleRel; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PathInfo[] p, ref uint modes, [Out] ModeInfo[] m, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref GetScale request);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref SetScale request);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

    // Windows' scaling steps; the API works with offsets relative to the monitor's recommended step.
    public static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    static SourceInfo Primary()
    {
        uint np, nm;
        GetDisplayConfigBufferSizes(2, out np, out nm); // QDC_ONLY_ACTIVE_PATHS
        var paths = new PathInfo[np];
        var modes = new ModeInfo[nm];
        int r = QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero);
        if (r != 0 || np == 0) throw new InvalidOperationException("QueryDisplayConfig failed: " + r);
        return paths[0].source;
    }

    // Returns { recommended %, current %, maximum % } of the primary display.
    public static int[] Scale()
    {
        SourceInfo s = Primary();
        var g = new GetScale();
        g.header.type = -3; // DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE
        g.header.size = (uint)Marshal.SizeOf(typeof(GetScale));
        g.header.adapterId = s.adapterId;
        g.header.id = s.id;
        int r = DisplayConfigGetDeviceInfo(ref g);
        if (r != 0) throw new InvalidOperationException("DisplayConfigGetDeviceInfo failed: " + r);
        int rec = -g.minRel;
        return new[] { Steps[rec], Steps[Math.Min(Steps.Length - 1, rec + g.curRel)], Steps[Math.Min(Steps.Length - 1, rec + g.maxRel)] };
    }

    public static bool SetScale(int percent)
    {
        int target = Array.IndexOf(Steps, percent);
        if (target < 0) return false;
        SourceInfo s = Primary();
        var g = new GetScale();
        g.header.type = -3;
        g.header.size = (uint)Marshal.SizeOf(typeof(GetScale));
        g.header.adapterId = s.adapterId;
        g.header.id = s.id;
        if (DisplayConfigGetDeviceInfo(ref g) != 0) return false;
        int rec = -g.minRel;
        if (target > rec + g.maxRel) return false;
        var set = new SetScale();
        set.header.type = -4; // DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE
        set.header.size = (uint)Marshal.SizeOf(typeof(SetScale));
        set.header.adapterId = s.adapterId;
        set.header.id = s.id;
        set.scaleRel = target - rec;
        return DisplayConfigSetDeviceInfo(ref set) == 0;
    }

    public static string Modes()
    {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        var seen = new System.Collections.Generic.SortedSet<string>();
        for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++) seen.Add(dm.dmPelsWidth + "x" + dm.dmPelsHeight);
        return string.Join(" ", seen);
    }

    public static bool SetResolution(int width, int height)
    {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++)
        {
            if (dm.dmPelsWidth == width && dm.dmPelsHeight == height)
            {
                dm.dmFields = 0x80000 | 0x100000; // DM_PELSWIDTH | DM_PELSHEIGHT
                return ChangeDisplaySettings(ref dm, 0) == 0;
            }
        }
        return false;
    }
}
"@
