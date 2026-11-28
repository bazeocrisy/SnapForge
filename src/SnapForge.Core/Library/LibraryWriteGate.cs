using System.Security.Cryptography;
using System.Text;

namespace SnapForge.Core.Library;

/// <summary>
/// The write gate of one <see cref="CaptureLibrary"/> instance: an in-process monitor plus a named OS mutex derived
/// from the library root, so that commits and clean-up are serialised across every SnapForge process that opens the
/// same Library folder (SnapForge Capture and SnapForge Editor). Re-entrant on the owning thread. Mutex ownership is
/// thread-affine, so every <see cref="Enter"/> must be disposed on the thread that entered (never across an await).
/// </summary>
internal sealed class LibraryWriteGate
{
    /// <summary>Default time to wait for another process to leave the gate.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly object _monitor = new();
    private readonly string _mutexName;
    private Mutex? _mutex;
    private int _depth;

    public LibraryWriteGate(string root)
    {
        _mutexName = MutexNameFor(root);
    }

    /// <summary>How long <see cref="Enter"/> waits for the cross-process mutex (tests shorten it).</summary>
    public TimeSpan Timeout { get; set; } = DefaultTimeout;

    /// <summary>True while the calling thread holds the gate.</summary>
    public bool IsHeldByCurrentThread => Monitor.IsEntered(_monitor);

    /// <summary>
    /// The mutex name for a library root: <c>Local\SnapForge-Library-</c> + the first 32 hex digits of the SHA-256 of
    /// the upper-cased full path (without a trailing separator), so every spelling of the same folder maps to one gate.
    /// </summary>
    public static string MutexNameFor(string root)
    {
        string normalized = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return @"Local\SnapForge-Library-" + Convert.ToHexString(hash, 0, 16);
    }

    /// <summary>
    /// Enters the gate: the in-process monitor (waiting as long as needed, as before) and, on the outermost entry, the
    /// cross-process mutex (waiting at most <see cref="Timeout"/>, then <see cref="IOException"/>). A mutex abandoned
    /// by a crashed process counts as acquired: the next index's recovery handles what it left behind.
    /// </summary>
    public Scope Enter()
    {
        Monitor.Enter(_monitor);
        try
        {
            if (_depth == 0)
            {
                AcquireMutex();
            }

            _depth++;
        }
        catch
        {
            Monitor.Exit(_monitor);
            throw;
        }

        return new Scope(this);
    }

    private void AcquireMutex()
    {
        _mutex ??= new Mutex(initiallyOwned: false, _mutexName);
        bool acquired;
        try
        {
            acquired = _mutex.WaitOne(Timeout);
        }
        catch (AbandonedMutexException)
        {
            acquired = true; // the previous holder crashed; ownership passed to this thread
        }

        if (!acquired)
        {
            throw new IOException("The Capture Library is busy in another SnapForge program. Try again in a moment.");
        }
    }

    private void Exit()
    {
        try
        {
            if (--_depth == 0)
            {
                _mutex!.ReleaseMutex();
            }
        }
        finally
        {
            Monitor.Exit(_monitor);
        }
    }

    /// <summary>Leaves the gate when disposed (on the entering thread).</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly LibraryWriteGate? _gate;

        internal Scope(LibraryWriteGate gate) => _gate = gate;

        public void Dispose() => _gate?.Exit();
    }
}
