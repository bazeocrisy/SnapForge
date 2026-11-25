using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SnapForge.App.Capture;
using SnapForge.App.Interop;
using SnapForge.App.Rendering;
using SnapForge.Core;
using SnapForge.Core.Annotations;
using SnapForge.Core.Capture;
using SnapForge.Core.Editing;
using SnapForge.Core.Export;
using SnapForge.Core.Geometry;
using CaptureMode = SnapForge.Core.Capture.CaptureMode;
using SnapForge.Core.Imaging;

namespace SnapForge.App;

public partial class MainWindow : Window
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 8;

    /// <summary>Time for the desktop to repaint after the main window hides (transitions are disabled, so this is short).</summary>
    private static readonly TimeSpan HideSettleDelay = TimeSpan.FromMilliseconds(180);

    private EditorDocument? _document;
    private DateTime _capturedAt;
    private string? _lastSaveDirectory;
    private bool _fitMode = true;
    private bool _busy;
    private bool _exportedSinceLoad;

    public MainWindow()
    {
        InitializeComponent();
        Title = ProductInfo.Name;
        Editor.StateChanged += (_, _) => UpdateCommandState();
        SourceInitialized += (_, _) =>
        {
            WindowChrome.UseDarkTitleBar(this);
            WindowChrome.DisableTransitions(this);
        };
        Loaded += (_, _) => DescribeDisplays();
    }

    internal EditorDocument? Document => _document;

    /// <summary>Loads a captured image into the editor (also used by tests).</summary>
    internal void LoadImage(PixelBuffer image, DateTime capturedAt)
    {
        EditorDocument? previous = _document;
        _document = new EditorDocument(image);
        _document.Changed += OnDocumentChanged;
        _exportedSinceLoad = false;
        _capturedAt = capturedAt;
        Editor.Load(_document);
        StartScreen.Visibility = Visibility.Collapsed;
        Viewport.Visibility = Visibility.Visible;
        OptionsBar.IsEnabled = true;
        ToolPalette.IsEnabled = true;
        ImageInfo.Text = $"{image.Width} × {image.Height} px";
        _fitMode = true;
        Dispatcher.BeginInvoke(ApplyFitZoom, System.Windows.Threading.DispatcherPriority.Loaded);
        UpdateCommandState();
        Editor.Focus();

        // The previous screenshot (and any original pixels kept for undoing its redactions) is no longer needed.
        if (previous is not null)
        {
            previous.Changed -= OnDocumentChanged;
            previous.Discard();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _document?.Discard();
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (e.ImageChanged && _exportedSinceLoad)
        {
            SetStatus("Redaction changed. Anything you already copied or saved still contains the earlier version.");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_busy)
        {
            // A capture or save is in progress; closing now could leave overlays or a partial temp file.
            e.Cancel = true;
            return;
        }

        Editor.CommitTextEdit();
        if (!e.Cancel && !ConfirmDiscard("close SnapForge"))
        {
            e.Cancel = true;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_document is null)
        {
            return;
        }

        // "100 %" means actual pixels on the current monitor, which just changed.
        if (_fitMode)
        {
            ApplyFitZoom();
        }
        else
        {
            SetZoom(ZoomTransform.ScaleX, fitMode: false);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_busy)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        bool alt = mods.HasFlag(ModifierKeys.Alt);
        bool typing = Editor.IsEditingText;

        if (ctrl && !alt)
        {
            switch (key)
            {
                case Key.D1 or Key.NumPad1: Handle(e, () => _ = CaptureAsync(CaptureMode.Region)); return;
                case Key.D2 or Key.NumPad2: Handle(e, () => _ = CaptureAsync(CaptureMode.Window)); return;
                case Key.D3 or Key.NumPad3: Handle(e, () => _ = CaptureAsync(CaptureMode.SingleScreen)); return;
                case Key.D4 or Key.NumPad4: Handle(e, () => _ = CaptureAsync(CaptureMode.AllScreens)); return;
                case Key.S when _document is not null: Handle(e, () => _ = SaveAsync()); return;
            }

            if (!typing && _document is not null)
            {
                switch (key)
                {
                    case Key.Z when shift: Handle(e, Redo); return;
                    case Key.Z: Handle(e, Undo); return;
                    case Key.Y: Handle(e, Redo); return;
                    case Key.C: Handle(e, () => _ = CopyAsync()); return;
                    case Key.D0 or Key.NumPad0: Handle(e, ApplyFitZoom); return;
                    case Key.OemPlus or Key.Add: Handle(e, () => ZoomBy(1.25)); return;
                    case Key.OemMinus or Key.Subtract: Handle(e, () => ZoomBy(1 / 1.25)); return;
                }
            }
        }

        if (ctrl && alt && (key is Key.D0 or Key.NumPad0) && _document is not null)
        {
            Handle(e, ApplyActualPixelsZoom);
            return;
        }

        if (typing || ctrl || alt || _document is null)
        {
            return;
        }

        switch (key)
        {
            case Key.Escape: if (Editor.CancelCurrentOperation()) { e.Handled = true; } return;
            case Key.Delete or Key.Back: Handle(e, Editor.DeleteSelection); return;
            case Key.V: Handle(e, () => SelectTool.IsChecked = true); return;
            case Key.A: Handle(e, () => ArrowTool.IsChecked = true); return;
            case Key.L: Handle(e, () => LineTool.IsChecked = true); return;
            case Key.R: Handle(e, () => RectangleTool.IsChecked = true); return;
            case Key.E: Handle(e, () => EllipseTool.IsChecked = true); return;
            case Key.T: Handle(e, () => TextTool.IsChecked = true); return;
            case Key.N: Handle(e, () => StepTool.IsChecked = true); return;
            case Key.X: Handle(e, () => RedactTool.IsChecked = true); return;
        }
    }

    private static void Handle(KeyEventArgs e, Action action)
    {
        e.Handled = true;
        action();
    }

    // ---------------------------------------------------------------- capture

    private void OnCaptureRegion(object sender, RoutedEventArgs e) => _ = CaptureAsync(CaptureMode.Region);

    private void OnCaptureWindow(object sender, RoutedEventArgs e) => _ = CaptureAsync(CaptureMode.Window);

    private void OnCaptureDisplay(object sender, RoutedEventArgs e) => _ = CaptureAsync(CaptureMode.SingleScreen);

    private void OnCaptureAll(object sender, RoutedEventArgs e) => _ = CaptureAsync(CaptureMode.AllScreens);

    private async Task CaptureAsync(CaptureMode mode)
    {
        if (_busy)
        {
            return;
        }

        Editor.CommitTextEdit();
        if (!ConfirmDiscard("take a new screenshot"))
        {
            return;
        }

        _busy = true;
        WindowState previousState = WindowState;
        try
        {
            // Get our own window out of the picture before copying the screen.
            Hide();
            await Task.Delay(HideSettleDelay).ConfigureAwait(true);

            CaptureSnapshot snapshot = ScreenCaptureService.TakeSnapshot();
            PixelRect? selection = mode == CaptureMode.AllScreens
                ? snapshot.Layout.VirtualBounds
                : await OverlaySession.RunAsync(snapshot, mode).ConfigureAwait(true);

            PixelBuffer? image = null;
            if (selection is { } screenRect)
            {
                PixelRect bitmapRect = snapshot.Layout.ClipSelectionToBitmap(screenRect);
                image = bitmapRect == snapshot.Image.Bounds ? snapshot.Image : snapshot.Image.Crop(bitmapRect);
            }

            // The full-desktop frame usually holds far more than the user selected; zero it once it is not the result.
            if (!ReferenceEquals(image, snapshot.Image))
            {
                Array.Clear(snapshot.Image.Pixels);
            }

            if (image is not null)
            {
                LoadImage(image, snapshot.CapturedAt);
                SetStatus(mode switch
                {
                    CaptureMode.AllScreens => $"Captured {snapshot.Layout.Monitors.Count} display(s).",
                    CaptureMode.SingleScreen => "Captured display.",
                    CaptureMode.Window => "Captured window.",
                    _ => "Captured region.",
                });
            }
            else
            {
                SetStatus("Capture cancelled.");
            }
        }
#pragma warning disable CA1031 // Any capture failure must bring the window back with a readable message.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            RestoreWindow(previousState);
            MessageBox.Show(this, $"The screen could not be captured.\n\n{ex.Message}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Capture failed.");
        }
        finally
        {
            RestoreWindow(previousState);
            _busy = false;
        }
    }

    private void RestoreWindow(WindowState state)
    {
        if (IsVisible)
        {
            return;
        }

        Show();
        WindowState = state == WindowState.Minimized ? WindowState.Normal : state;
        Activate();
        Editor.Focus();
    }

    // ---------------------------------------------------------------- editing

    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out AnnotationTool tool) && Editor is not null)
        {
            Editor.SetTool(tool);
            SetStatus(tool switch
            {
                AnnotationTool.Select => "Click an annotation to select it; drag to move. Double-click text to edit. Delete removes it.",
                AnnotationTool.Text => "Click to place text. Enter to finish, Shift+Enter for a new line, Esc to cancel.",
                AnnotationTool.StepMarker => "Click to place the next numbered step.",
                AnnotationTool.Redact => "Drag over sensitive content. You can undo while editing; once copied or saved, the exported image contains only the black box.",
                _ => "Drag to draw. Hold Shift to constrain.",
            });
        }
    }

    private void OnColorChecked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex } && Editor is not null)
        {
            Editor.SetColor(ArgbColor.Parse(hex));
        }
    }

    private void OnSizeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value } && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double width) && Editor is not null)
        {
            Editor.SetStrokeWidth(width);
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();

    private void OnRedo(object sender, RoutedEventArgs e) => Redo();

    private void OnDeleteSelection(object sender, RoutedEventArgs e) => Editor.DeleteSelection();

    private void Undo()
    {
        Editor.CancelGesture();
        Editor.CommitTextEdit();
        string? what = _document?.History.UndoDescription;
        if (_document?.Undo() == true)
        {
            SetStatus($"Undid: {what}");
        }
    }

    private void Redo()
    {
        Editor.CancelGesture();
        Editor.CommitTextEdit();
        string? what = _document?.History.RedoDescription;
        if (_document?.Redo() == true)
        {
            SetStatus($"Redid: {what}");
        }
    }

    private void UpdateCommandState()
    {
        bool hasDoc = _document is not null;
        UndoButton.IsEnabled = hasDoc && _document!.History.CanUndo;
        RedoButton.IsEnabled = hasDoc && _document!.History.CanRedo;
        UndoButton.ToolTip = hasDoc && _document!.History.UndoDescription is { } u ? $"Undo {u} (Ctrl+Z)" : "Undo (Ctrl+Z)";
        RedoButton.ToolTip = hasDoc && _document!.History.RedoDescription is { } r ? $"Redo {r} (Ctrl+Y)" : "Redo (Ctrl+Y)";
        CopyButton.IsEnabled = hasDoc;
        SaveButton.IsEnabled = hasDoc;
        DeleteButton.IsEnabled = Editor.HasSelection;
        Title = hasDoc && _document!.IsDirty ? $"{ProductInfo.Name} • unsaved changes" : ProductInfo.Name;
    }

    // ---------------------------------------------------------------- output

    private void OnCopy(object sender, RoutedEventArgs e) => _ = CopyAsync();

    private void OnSave(object sender, RoutedEventArgs e) => _ = SaveAsync();

    internal PixelBuffer RenderFinalImage()
    {
        if (_document is null)
        {
            throw new InvalidOperationException("Nothing to export.");
        }

        Editor.CommitTextEdit();
        return ImageExporter.Flatten(_document.Image, _document.Annotations);
    }

    private async Task CopyAsync()
    {
        if (_document is null || _busy)
        {
            return;
        }

        try
        {
            await ImageExporter.CopyToClipboardAsync(RenderFinalImage()).ConfigureAwait(true);
            _exportedSinceLoad = true;
            SetStatus("Copied to clipboard.");
        }
#pragma warning disable CA1031 // Report clipboard failures instead of crashing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MessageBox.Show(this, $"The image could not be copied to the clipboard.\n\n{ex.Message}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SaveAsync()
    {
        if (_document is null || _busy)
        {
            return;
        }

        Editor.CommitTextEdit();
        var dialog = new SaveFileDialog
        {
            Title = "Save screenshot",
            Filter = ExportRules.SaveDialogFilter,
            FilterIndex = 1,
            FileName = ExportRules.DefaultFileName(_capturedAt, ImageFileFormat.Png),
            InitialDirectory = _lastSaveDirectory ?? DefaultSaveDirectory(),
            AddExtension = true,
            DefaultExt = ".png",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        (string path, ImageFileFormat format) = ExportRules.ResolveSaveTarget(dialog.FileName, dialog.FilterIndex);
        if (path != dialog.FileName && File.Exists(path) &&
            MessageBox.Show(this, $"{Path.GetFileName(path)} already exists.\nDo you want to replace it?", ProductInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _busy = true;
            await ImageExporter.SaveAsync(RenderFinalImage(), path, format).ConfigureAwait(true);
            _document.MarkSaved();
            _exportedSinceLoad = true;
            _lastSaveDirectory = Path.GetDirectoryName(path);
            SetStatus($"Saved {path}");
        }
#pragma warning disable CA1031 // Report I/O failures (permissions, disk full) instead of crashing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MessageBox.Show(this, $"The image could not be saved.\n\n{ex.Message}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            UpdateCommandState();
        }
    }

    private static string DefaultSaveDirectory()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string screenshots = Path.Combine(pictures, "Screenshots");
        return Directory.Exists(screenshots) ? screenshots : pictures;
    }

    private bool ConfirmDiscard(string action)
    {
        if (_document is null || !_document.IsDirty)
        {
            return true;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"The current screenshot has changes that have not been saved.\n\nDiscard them and {action}?",
            ProductInfo.Name,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
    }

    // ---------------------------------------------------------------- zoom

    private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomBy(1.25);

    private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);

    private void OnZoomFit(object sender, RoutedEventArgs e) => ApplyFitZoom();

    private void OnZoomActual(object sender, RoutedEventArgs e) => ApplyActualPixelsZoom();

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _document is not null)
        {
            ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15);
            e.Handled = true;
        }
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitMode)
        {
            ApplyFitZoom();
        }
    }

    private void ApplyFitZoom()
    {
        if (_document is null)
        {
            return;
        }

        double availableW = Math.Max(1, Viewport.ActualWidth - 50);
        double availableH = Math.Max(1, Viewport.ActualHeight - 50);

        // Never upscale small captures when fitting; show them at actual pixels instead.
        double fit = Math.Min(availableW / _document.Image.Width, availableH / _document.Image.Height);
        SetZoom(Math.Min(fit, ActualPixelsZoom()), fitMode: true);
    }

    private void ApplyActualPixelsZoom() => SetZoom(ActualPixelsZoom(), fitMode: false);

    /// <summary>Zoom at which one image pixel maps to one physical screen pixel on the current monitor.</summary>
    private double ActualPixelsZoom() => 1 / VisualTreeHelper.GetDpi(this).DpiScaleX;

    private void ZoomBy(double factor) => SetZoom(ZoomTransform.ScaleX * factor, fitMode: false);

    private void SetZoom(double zoom, bool fitMode)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _fitMode = fitMode;
        ZoomTransform.ScaleX = zoom;
        ZoomTransform.ScaleY = zoom;
        Editor.Zoom = zoom;
        RenderOptions.SetBitmapScalingMode(Editor, zoom * VisualTreeHelper.GetDpi(this).DpiScaleX >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        Editor.InvalidateVisual();

        // Show zoom relative to actual pixels, which is what users mean by "100 %".
        ZoomText.Text = $"{zoom / ActualPixelsZoom() * 100:0}%";
    }

    // ---------------------------------------------------------------- misc

    private void SetStatus(string message) => StatusText.Text = message;

    private void DescribeDisplays()
    {
        try
        {
            ScreenLayout layout = ScreenCaptureService.GetScreenLayout();
            DisplaySummary.Text = layout.Monitors.Count == 1
                ? $"1 display · {layout.Monitors[0].Bounds.Width}×{layout.Monitors[0].Bounds.Height}"
                : $"{layout.Monitors.Count} displays · {string.Join("  ·  ", layout.Monitors.Select((m, i) => m.DisplayLabel(i)))}";
        }
#pragma warning disable CA1031 // Display summary is informational only.
        catch (Exception)
#pragma warning restore CA1031
        {
            DisplaySummary.Text = string.Empty;
        }
    }
}
