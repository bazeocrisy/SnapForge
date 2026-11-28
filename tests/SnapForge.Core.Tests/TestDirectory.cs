namespace SnapForge.Core.Tests;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snapforge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Stream that fails after a number of bytes, simulating a full disk or a crash mid-write.</summary>
internal sealed class FailingStream(Stream inner, long failAfterBytes, Func<Exception> failure) : Stream
{
    private long _written;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _written;
    public override long Position { get => _written; set => throw new NotSupportedException(); }

    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_written + count > failAfterBytes)
        {
            int allowed = (int)Math.Max(0, failAfterBytes - _written);
            inner.Write(buffer, offset, allowed);
            _written += allowed;
            throw failure();
        }

        inner.Write(buffer, offset, count);
        _written += count;
    }
}
