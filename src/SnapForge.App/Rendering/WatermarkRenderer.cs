using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Rendering;

/// <summary>Renders watermark text (white with a dark outline, readable on light and dark images) to a transparent image.</summary>
internal static class WatermarkRenderer
{
    public static PixelBuffer Render(string text, double fontSize)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.White, 1.0);
        Geometry glyphs = ft.BuildGeometry(new Point(4, 2));
        int w = (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 8, h = (int)Math.Ceiling(ft.Height) + 4;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)), Math.Max(2, fontSize / 10)) { LineJoin = PenLineJoin.Round }, glyphs);
            dc.DrawGeometry(Brushes.White, null, glyphs);
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        byte[] px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        // Premultiplied → straight alpha (ImageOps.DrawImage blends straight-alpha sources).
        for (int i = 0; i < px.Length; i += 4)
        {
            byte a = px[i + 3];
            if (a is > 0 and < 255)
            {
                px[i] = (byte)Math.Min(255, px[i] * 255 / a);
                px[i + 1] = (byte)Math.Min(255, px[i + 1] * 255 / a);
                px[i + 2] = (byte)Math.Min(255, px[i + 2] * 255 / a);
            }
        }

        return new PixelBuffer(w, h, px);
    }
}
