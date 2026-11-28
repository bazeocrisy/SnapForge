namespace SnapForge.Core.Imaging;

/// <summary>
/// GIF-flavoured variable-code-size LZW compressor (GIF89a specification, appendix F).
/// </summary>
/// <remarks>
/// Codes start at <c>minCodeSize + 1</c> bits and grow to at most 12. The stream begins with a clear code;
/// when all 4096 codes are in use a clear code is emitted and the table restarts. The code width used for
/// each code mirrors the decoder, which adds its table entries one code later than the encoder does.
/// Output is packed least-significant-bit first into data sub-blocks of at most 255 bytes, followed by the
/// zero-length block terminator.
/// </remarks>
internal sealed class GifLzwEncoder
{
    private const int MaxCodes = 4096;
    private const int HashSize = 8192; // power of two, at least 2× the table size
    private readonly int[] _hashKeys = new int[HashSize];
    private readonly short[] _hashValues = new short[HashSize];
    private readonly byte[] _block = new byte[256];

    private Stream _output = Stream.Null;
    private int _blockLength;
    private int _bitBuffer;
    private int _bitCount;

    /// <summary>Compresses <paramref name="indices"/> (each &lt; 2^minCodeSize) and writes the sub-blocks.</summary>
    public void Encode(Stream output, ReadOnlySpan<byte> indices, int minCodeSize)
    {
        if (minCodeSize is < 2 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(minCodeSize));
        }

        _output = output;
        _blockLength = 0;
        _bitBuffer = 0;
        _bitCount = 0;

        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;
        int initialWidth = minCodeSize + 1;

        // Encoder table state.
        int nextCode = endCode + 1;
        ResetTable();

        // Mirror of the decoder: it adds an entry for every code after the first one following a clear.
        int decoderNext = endCode + 1;
        bool decoderFirst = true;

        void Emit(int code)
        {
            int width = Math.Max(initialWidth, Math.Min(12, BitLength(decoderNext)));
            WriteBits(code, width);
            if (code == clearCode)
            {
                decoderNext = endCode + 1;
                decoderFirst = true;
            }
            else if (decoderFirst)
            {
                decoderFirst = false;
            }
            else if (decoderNext < MaxCodes)
            {
                decoderNext++;
            }
        }

        Emit(clearCode);
        if (indices.IsEmpty)
        {
            Emit(endCode);
            FlushBits();
            return;
        }

        int current = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int k = indices[i];
            int key = (current << 8) | k; // current < 4096, so the key fits in 20 bits
            int slot = (int)(((uint)key * 2654435761u) >> 19);
            while (true)
            {
                int existing = _hashKeys[slot];
                if (existing == key + 1)
                {
                    current = _hashValues[slot];
                    goto next;
                }

                if (existing == 0)
                {
                    break;
                }

                slot = (slot + 1) & (HashSize - 1);
            }

            Emit(current);
            if (nextCode < MaxCodes)
            {
                _hashKeys[slot] = key + 1;
                _hashValues[slot] = (short)nextCode;
                nextCode++;
            }
            else
            {
                Emit(clearCode);
                ResetTable();
                nextCode = endCode + 1;
            }

            current = k;
        next:;
        }

        Emit(current);
        Emit(endCode);
        FlushBits();
    }

    private static int BitLength(int value) => 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)value);

    private void ResetTable() => Array.Clear(_hashKeys);

    private void WriteBits(int code, int width)
    {
        _bitBuffer |= code << _bitCount;
        _bitCount += width;
        while (_bitCount >= 8)
        {
            WriteByte((byte)_bitBuffer);
            _bitBuffer >>= 8;
            _bitCount -= 8;
        }
    }

    private void FlushBits()
    {
        if (_bitCount > 0)
        {
            WriteByte((byte)_bitBuffer);
            _bitBuffer = 0;
            _bitCount = 0;
        }

        FlushBlock();
        _output.WriteByte(0); // block terminator
    }

    private void WriteByte(byte value)
    {
        _block[1 + _blockLength++] = value;
        if (_blockLength == 255)
        {
            FlushBlock();
        }
    }

    private void FlushBlock()
    {
        if (_blockLength == 0)
        {
            return;
        }

        _block[0] = (byte)_blockLength;
        _output.Write(_block, 0, _blockLength + 1);
        _blockLength = 0;
    }
}
