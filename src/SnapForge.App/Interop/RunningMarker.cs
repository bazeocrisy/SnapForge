using System.Runtime.InteropServices;

namespace SnapForge.App.Interop;

/// <summary>
/// FA-002: a machine-wide "SnapForge is running" marker (Global\SnapForge-Running-7c1d) that the system-wide installer
/// and uninstaller check through Inno Setup's AppMutex, so they refuse to replace files while SnapForge runs in ANY
/// user session (they never kill it — it could be recording). The mutex is created with a security descriptor that
/// lets every user open it, so a second user's SnapForge can share it instead of failing with "access denied".
/// It is only a presence marker: nobody waits on it.
/// </summary>
internal static partial class RunningMarker
{
    public const string Name = @"Global\SnapForge-Running-7c1d";

    // Everyone: synchronize + query (open/hold); owner rights for the creator. Low integrity is not needed.
    private const string Sddl = "D:(A;;0x1F0001;;;WD)(A;;0x1F0001;;;SY)(A;;0x1F0001;;;BA)";

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public nint lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSddl(string sddl, uint revision, out nint descriptor, nint size);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateMutexW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateMutex(ref SECURITY_ATTRIBUTES attributes, [MarshalAs(UnmanagedType.Bool)] bool initialOwner, string name);

    [LibraryImport("kernel32.dll", EntryPoint = "OpenMutexW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint OpenMutex(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint mem);

    private const uint SYNCHRONIZE = 0x00100000;

    private static nint _handle;

    /// <summary>Creates or opens the marker and keeps a handle for the life of the process (best effort).</summary>
    public static void Hold()
    {
        if (_handle != 0)
        {
            return;
        }

        if (!ConvertSddl(Sddl, 1, out nint sd, 0))
        {
            return;
        }

        try
        {
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
            nint h = CreateMutex(ref sa, false, Name);
            _handle = h != 0 ? h : OpenMutex(SYNCHRONIZE, false, Name); // created by another user with an older build
        }
        finally
        {
            LocalFree(sd);
        }
    }

    /// <summary>True if any process in any session holds the marker (tests).</summary>
    public static bool IsHeldAnywhere()
    {
        nint h = OpenMutex(SYNCHRONIZE, false, Name);
        if (h == 0)
        {
            return false;
        }

        CloseHandle(h);
        return true;
    }
}
