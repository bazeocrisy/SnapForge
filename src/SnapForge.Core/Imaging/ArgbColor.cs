using System.Globalization;

namespace SnapForge.Core.Imaging;

/// <summary>A non-premultiplied 8-bit-per-channel color.</summary>
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public static ArgbColor Black => new(255, 0, 0, 0);
    public static ArgbColor White => new(255, 255, 255, 255);

    public static ArgbColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    /// <summary>Parses #RRGGBB or #AARRGGBB.</summary>
    public static ArgbColor Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        string s = hex.StartsWith('#') ? hex[1..] : hex;
        uint value = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return s.Length switch
        {
            6 => new ArgbColor(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value),
            8 => new ArgbColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value),
            _ => throw new FormatException($"Expected #RRGGBB or #AARRGGBB, got '{hex}'."),
        };
    }

    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}
