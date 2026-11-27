using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapForge.App.Dialogs;

namespace SnapForge.App.Video;

/// <summary>GIF export options chosen by the user.</summary>
internal sealed record GifExportSettings(int FramesPerSecond, int MaxWidth, bool Dither, bool Loop);

/// <summary>Video → Export GIF: frame rate, width, dithering and looping, with a duration warning.</summary>
internal sealed class GifExportDialog : ImageDialogBase
{
    private readonly ComboBox _fps;
    private readonly TextBox _width;
    private readonly CheckBox _dither;
    private readonly CheckBox _loop;
    private readonly int _sourceWidth;

    private GifExportDialog(int sourceWidth, long durationMs)
        : base("Export GIF")
    {
        _sourceWidth = sourceWidth;
        _fps = new ComboBox { ItemsSource = new[] { 5, 10, 15, 20, 25 }, SelectedItem = 10, Width = 90 };
        AutomationProperties.SetName(_fps, "Frames per second");
        _width = NumberBox("Width in pixels", Math.Min(sourceWidth, 800));
        _dither = new CheckBox { Content = "_Dither (smoother gradients, larger file)", IsChecked = false, Margin = new Thickness(0, 4, 0, 0) };
        _loop = new CheckBox { Content = "_Loop forever", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
        var body = new StackPanel();
        string warning = durationMs > 60_000
            ? $" The kept range is {durationMs / 1000} s long: GIFs of long recordings become very large — trim first if you can."
            : string.Empty;
        body.Children.Add(new TextBlock
        {
            Text = "GIFs have at most 256 colours and no sound." + warning,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Opacity = 0.85,
        });
        body.Children.Add(Row(("_Frame rate:", _fps), ("_Width (px):", _width)));
        body.Children.Add(_dither);
        body.Children.Add(_loop);
        Build(body, () => TryRead(_width, 16, Math.Max(16, _sourceWidth), "Width", out _));
    }

    public static GifExportSettings? Ask(Window owner, int sourceWidth, long durationMs)
    {
        var d = new GifExportDialog(Math.Max(16, sourceWidth), durationMs);
        d.Owner = owner;
        if (d.ShowDialog() != true)
        {
            return null;
        }

        int width = int.Parse(d._width.Text, NumberStyles.Integer, CultureInfo.CurrentCulture);
        return new GifExportSettings((int)(d._fps.SelectedItem ?? 10), width, d._dither.IsChecked == true, d._loop.IsChecked == true);
    }
}
