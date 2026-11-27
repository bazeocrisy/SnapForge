using System.IO;
using SnapForge.App.Video;

namespace SnapForge.App.Tests;

/// <summary>FA-022: saving a recording outside the Library — verified temporary copy, atomic replace, nothing lost on failure.</summary>
public sealed class RecordingSaveOutsideTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapforge-save-" + Guid.NewGuid().ToString("N"));

    public RecordingSaveOutsideTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Copies_a_verified_file_and_leaves_no_temporary_file()
    {
        string source = Write("library.mp4", 3 * 1024 * 1024 + 17);
        string target = Path.Combine(_dir, "out", "saved.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        VerifiedFileCopy.Copy(source, target);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
        Assert.True(File.Exists(source)); // deleting the Library copy is the caller's job, after success
        Assert.DoesNotContain(Directory.GetFiles(Path.GetDirectoryName(target)!), f => VerifiedFileCopy.IsTempName(Path.GetFileName(f)));
    }

    [Fact]
    public void Replaces_an_existing_destination_only_after_verification()
    {
        string source = Write("library.mp4", 512 * 1024);
        string target = Write("existing.mp4", 1000, seed: 9);
        VerifiedFileCopy.Copy(source, target);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
        AssertNoTemp();
    }

    [Fact]
    public void Disk_full_while_copying_keeps_the_existing_destination_and_the_source()
    {
        string source = Write("library.mp4", 2 * 1024 * 1024);
        string target = Write("existing.mp4", 4096, seed: 3);
        byte[] before = File.ReadAllBytes(target);
        byte[] sourceBefore = File.ReadAllBytes(source);
        IOException e = Assert.ThrowsAny<IOException>(() => VerifiedFileCopy.Copy(source, target, s => new FailingStream(s, failAfter: 1024 * 1024)));
        Assert.Contains("not enough space", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(target));
        Assert.Equal(sourceBefore, File.ReadAllBytes(source));
        AssertNoTemp();
    }

    [Fact]
    public void Corrupted_copy_is_rejected_and_nothing_is_replaced()
    {
        string source = Write("library.mp4", 256 * 1024);
        string target = Path.Combine(_dir, "new.mp4");
        Assert.Throws<CopyVerificationException>(() => VerifiedFileCopy.Copy(source, target, s => new CorruptingStream(s)));
        Assert.False(File.Exists(target));
        AssertNoTemp();
    }

    [Fact]
    public void Missing_destination_folder_fails_cleanly()
    {
        string source = Write("library.mp4", 1024);
        Assert.ThrowsAny<IOException>(() => VerifiedFileCopy.Copy(source, Path.Combine(_dir, "no-such-folder", "x.mp4")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Copy_onto_itself_is_refused()
    {
        string source = Write("library.mp4", 1024);
        Assert.ThrowsAny<IOException>(() => VerifiedFileCopy.Copy(source, source));
        Assert.Equal(1024, new FileInfo(source).Length);
    }

    [Fact]
    public void Temporary_name_is_hidden_style_unique_and_in_the_destination_folder()
    {
        string target = Path.Combine(_dir, "clip.mp4");
        string a = VerifiedFileCopy.TempPathFor(target), b = VerifiedFileCopy.TempPathFor(target);
        Assert.NotEqual(a, b);
        Assert.Equal(_dir, Path.GetDirectoryName(a));
        Assert.StartsWith(".clip.mp4.snapforge-tmp-", Path.GetFileName(a), StringComparison.Ordinal);
        Assert.True(VerifiedFileCopy.IsTempName(Path.GetFileName(a)));
    }

    private void AssertNoTemp() => Assert.DoesNotContain(Directory.GetFiles(_dir), f => VerifiedFileCopy.IsTempName(Path.GetFileName(f)));

    private string Write(string name, int bytes, int seed = 1)
    {
        string path = Path.Combine(_dir, name);
        var data = new byte[bytes];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>Throws like a full disk after <c>failAfter</c> bytes.</summary>
    private sealed class FailingStream(Stream inner, long failAfter) : Stream
    {
        private long _written;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_written + count > failAfter)
            {
                throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
            }

            inner.Write(buffer, offset, count);
            _written += count;
        }
    }

    /// <summary>Flips one byte (a copy that silently differs from its source).</summary>
    private sealed class CorruptingStream(Stream inner) : Stream
    {
        private bool _done;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_done && count > 10)
            {
                byte[] copy = buffer[offset..(offset + count)];
                copy[10] ^= 0xFF;
                _done = true;
                inner.Write(copy, 0, count);
                return;
            }

            inner.Write(buffer, offset, count);
        }
    }
}
