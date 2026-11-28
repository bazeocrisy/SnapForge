using System.Runtime.InteropServices;

namespace SnapForge.App.Video.Interop;

/// <summary>
/// Minimal raw COM helpers for the video pipeline. Methods are called through their vtable slot (verified against the
/// Windows SDK interface definitions; see docs/VIDEO-ARCHITECTURE.md). Raw calls avoid a large projection dependency
/// and keep every native call explicit and reviewable.
/// </summary>
internal static unsafe class Com
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;

    /// <summary>The function pointer in vtable <paramref name="slot"/> of COM object <paramref name="obj"/>.</summary>
    public static void* Slot(nint obj, int slot) => (*(void***)obj)[slot];

    public static void Release(ref nint obj)
    {
        if (obj != 0)
        {
            Marshal.Release(obj);
            obj = 0;
        }
    }

    public static nint QueryInterface(nint obj, Guid iid)
    {
        int hr = Marshal.QueryInterface(obj, in iid, out nint result);
        Check(hr, "QueryInterface");
        return result;
    }

    public static nint TryQueryInterface(nint obj, Guid iid) =>
        Marshal.QueryInterface(obj, in iid, out nint result) == S_OK ? result : 0;

    public static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new NativeCallException(what, hr);
        }
    }

    // ---- common call shapes (this, args…) → HRESULT

    public static int Call(nint obj, int slot) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)Slot(obj, slot))(obj);

    public static int Call(nint obj, int slot, nint a) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(obj, slot))(obj, a);

    public static int Call(nint obj, int slot, nint a, nint b) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Slot(obj, slot))(obj, a, b);

    public static int Call(nint obj, int slot, int a, nint b) =>
        ((delegate* unmanaged[Stdcall]<nint, int, nint, int>)Slot(obj, slot))(obj, a, b);

    public static int Call(nint obj, int slot, int a, nint b, nint c) =>
        ((delegate* unmanaged[Stdcall]<nint, int, nint, nint, int>)Slot(obj, slot))(obj, a, b, c);

    public static int Call(nint obj, int slot, int a, int b, nint c) =>
        ((delegate* unmanaged[Stdcall]<nint, int, int, nint, int>)Slot(obj, slot))(obj, a, b, c);

    public static int Call(nint obj, int slot, Guid* a, nint b) =>
        ((delegate* unmanaged[Stdcall]<nint, Guid*, nint, int>)Slot(obj, slot))(obj, a, b);

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint obj);

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(nint p);

    public const uint CLSCTX_ALL = 0x17;
    public const uint COINIT_MULTITHREADED = 0x0;
}

/// <summary>WinRT activation without a projection: HSTRING + RoGetActivationFactory.</summary>
internal static unsafe class WinRt
{
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint classId, in Guid iid, out nint factory);

    [DllImport("combase.dll")]
    public static extern int RoInitialize(int initType);

    /// <summary>Activation factory (or statics) interface for a runtime class.</summary>
    public static nint GetActivationFactory(string runtimeClass, Guid iid)
    {
        Com.Check(WindowsCreateString(runtimeClass, runtimeClass.Length, out nint hs), "WindowsCreateString");
        try
        {
            Com.Check(RoGetActivationFactory(hs, in iid, out nint factory), $"RoGetActivationFactory({runtimeClass})");
            return factory;
        }
        finally
        {
            _ = WindowsDeleteString(hs);
        }
    }
}

/// <summary>A failed native (COM/WinRT/Media Foundation) call; <see cref="Exception.HResult"/> holds the HRESULT.</summary>
internal sealed class NativeCallException : Exception
{
    public NativeCallException()
    {
    }

    public NativeCallException(string message)
        : base(message)
    {
    }

    public NativeCallException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public NativeCallException(string what, int hr)
        : base($"{what} failed (0x{hr:X8}).")
    {
        HResult = hr;
    }
}
