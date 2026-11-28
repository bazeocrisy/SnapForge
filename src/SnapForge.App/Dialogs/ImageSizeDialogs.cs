using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SnapForge.App.Interop;
using SnapForge.App.Themes;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Dialogs;

/// <summary>Shared look for the small image dialogs (resize, canvas size, border).</summary>
internal abstract class ImageDialogBase : Window
{
    protected ImageDialogBase(string title)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (System.Windows.Media.FontFamily)FindResource("UiFont");
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
    }

    protected TextBlock Error { get; } = new() { Foreground = System.Windows.Media.Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    protected static TextBox NumberBox(string name, int value)
    {
        var box = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), Width = 90, Margin = new Thickness(0, 2, 0, 2) };
        AutomationProperties.SetName(box, name);
        return box;
    }

    protected static Grid Row(params (string Label, FrameworkElement Control)[] rows)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < rows.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new Label { Content = rows[i].Label, Target = rows[i].Control, Padding = new Thickness(0, 4, 8, 4) };
            Grid.SetRow(label, i);
            g.Children.Add(label);
            Grid.SetRow(rows[i].Control, i);
            Grid.SetColumn(rows[i].Control, 1);
            g.Children.Add(rows[i].Control);
        }

        return g;
    }

    protected void Build(UIElement body, Func<bool> accept)
    {
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) =>
        {
            if (accept())
            {
                DialogResult = true;
            }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(body);
        root.Children.Add(Error);
        root.Children.Add(buttons);
        Content = root;
    }

    protected bool TryRead(TextBox box, int min, int max, string what, out int value)
    {
        if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) || value < min || value > max)
        {
            Error.Text = $"{what} must be a whole number from {min} to {max}.";
            box.Focus();
            box.SelectAll();
            return false;
        }

        return true;
    }

    protected bool? ShowFor(Window owner)
    {
        Owner = owner;
        return ShowDialog();
    }
}

/// <summary>Image → Resize image: pixels or percent, aspect ratio locked by default.</summary>
internal sealed class ResizeImageDialog : ImageDialogBase
{
    private readonly TextBox _width, _height, _percent;
    private readonly CheckBox _lock;
    private readonly int _w0, _h0;
    private bool _updating;

    private ResizeImageDialog(int width, int height)
        : base("Resize image")
    {
        _w0 = width;
        _h0 = height;
        _width = NumberBox("Width in pixels", width);
        _height = NumberBox("Height in pixels", height);
        _percent = NumberBox("Size in percent", 100);
        _lock = new CheckBox { Content = "Keep _aspect ratio", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
        _width.TextChanged += (_, _) => Sync(fromWidth: true);
        _height.TextChanged += (_, _) => Sync(fromWidth: false);
        _percent.TextChanged += (_, _) => SyncPercent();
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = $"Current size: {width} × {height} pixels", Margin = new Thickness(0, 0, 0, 10), Opacity = 0.85 });
        body.Children.Add(Row(("_Width (px):", _width), ("_Height (px):", _height), ("_Percent:", _percent)));
        body.Children.Add(_lock);
        Build(body, Accept);
        Loaded += (_, _) => { _width.Focus(); _width.SelectAll(); };
    }

    public int ResultWidth { get; private set; }

    public int ResultHeight { get; private set; }

    public static (int Width, int Height)? Ask(Window owner, int width, int height)
    {
        var d = new ResizeImageDialog(width, height);
        return d.ShowFor(owner) == true ? (d.ResultWidth, d.ResultHeight) : null;
    }

    private void Sync(bool fromWidth)
    {
        if (_updating || _lock.IsChecked != true)
        {
            return;
        }

        _updating = true;
        if (fromWidth && int.TryParse(_width.Text, out int w) && w > 0)
        {
            _height.Text = Math.Max(1, (int)Math.Round(w * (double)_h0 / _w0)).ToString(CultureInfo.CurrentCulture);
            _percent.Text = Math.Round(w * 100.0 / _w0).ToString(CultureInfo.CurrentCulture);
        }
        else if (!fromWidth && int.TryParse(_height.Text, out int h) && h > 0)
        {
            _width.Text = Math.Max(1, (int)Math.Round(h * (double)_w0 / _h0)).ToString(CultureInfo.CurrentCulture);
            _percent.Text = Math.Round(h * 100.0 / _h0).ToString(CultureInfo.CurrentCulture);
        }

        _updating = false;
    }

    private void SyncPercent()
    {
        if (_updating || !int.TryParse(_percent.Text, out int p) || p <= 0)
        {
            return;
        }

        _updating = true;
        PixelSize s = ImageOps.ScaleDimensions(_w0, _h0, p);
        _width.Text = s.Width.ToString(CultureInfo.CurrentCulture);
        _height.Text = s.Height.ToString(CultureInfo.CurrentCulture);
        _updating = false;
    }

    private bool Accept()
    {
        if (!TryRead(_width, 1, ImageOps.MaxDimension, "Width", out int w) || !TryRead(_height, 1, ImageOps.MaxDimension, "Height", out int h))
        {
            return false;
        }

        if ((long)w * h > ImageOps.MaxPixelCount)
        {
            Error.Text = $"That image would be too large (over {ImageOps.MaxPixelCount:N0} pixels).";
            return false;
        }

        ResultWidth = w;
        ResultHeight = h;
        return true;
    }
}

/// <summary>Image → Canvas size: grows or crops the canvas around a chosen anchor; new area filled with a colour.</summary>
internal sealed class CanvasSizeDialog : ImageDialogBase
{
    private readonly TextBox _width, _height;
    private readonly UniformGrid _anchors = new() { Rows = 3, Columns = 3, Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left };
    private CanvasAnchor _anchor = CanvasAnchor.Center;

    private CanvasSizeDialog(int width, int height)
        : base("Canvas size")
    {
        _width = NumberBox("Canvas width in pixels", width);
        _height = NumberBox("Canvas height in pixels", height);
        foreach (CanvasAnchor a in Enum.GetValues<CanvasAnchor>())
        {
            var rb = new RadioButton { GroupName = "anchor", IsChecked = a == CanvasAnchor.Center, Margin = new Thickness(6), ToolTip = a.ToString() };
            AutomationProperties.SetName(rb, $"Anchor {a}");
            CanvasAnchor captured = a;
            rb.Checked += (_, _) => _anchor = captured;
            _anchors.Children.Add(rb);
        }

        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = $"Current size: {width} × {height} pixels. New space is filled with white; a smaller canvas cuts off the edges.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Opacity = 0.85 });
        body.Children.Add(Row(("_Width (px):", _width), ("_Height (px):", _height)));
        body.Children.Add(new TextBlock { Text = "Keep the image at:", Margin = new Thickness(0, 4, 0, 4) });
        body.Children.Add(_anchors);
        Build(body, Accept);
        Loaded += (_, _) => { _width.Focus(); _width.SelectAll(); };
    }

    public static (int Width, int Height, CanvasAnchor Anchor)? Ask(Window owner, int width, int height)
    {
        var d = new CanvasSizeDialog(width, height);
        return d.ShowFor(owner) == true ? (d.W, d.H, d._anchor) : null;
    }

    private int W { get; set; }

    private int H { get; set; }

    private bool Accept()
    {
        if (!TryRead(_width, 1, ImageOps.MaxDimension, "Width", out int w) || !TryRead(_height, 1, ImageOps.MaxDimension, "Height", out int h))
        {
            return false;
        }

        W = w;
        H = h;
        return true;
    }
}

/// <summary>Image → Add border: thickness in pixels (colour = current annotation colour).</summary>
internal sealed class BorderDialog : ImageDialogBase
{
    private readonly TextBox _thickness;

    private BorderDialog()
        : base("Add border")
    {
        _thickness = NumberBox("Border thickness in pixels", 4);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "The border uses the current colour from Quick Styles and grows the image.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Opacity = 0.85 });
        body.Children.Add(Row(("_Thickness (px):", _thickness)));
        Build(body, () => TryRead(_thickness, 1, 500, "Thickness", out _));
        Loaded += (_, _) => { _thickness.Focus(); _thickness.SelectAll(); };
    }

    public static int? Ask(Window owner)
    {
        var d = new BorderDialog();
        return d.ShowFor(owner) == true && int.TryParse(d._thickness.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int t) ? t : null;
    }
}
