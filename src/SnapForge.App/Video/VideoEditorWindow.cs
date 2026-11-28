using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapForge.App.Interop;
using SnapForge.App.Video.Interop;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.Core;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using SnapForge.Core.Video;

namespace SnapForge.App.Video;

/// <summary>
/// Video editing for a Library recording: preview and precise seeking, trim start/end, remove sections, save the
/// current frame to the Library as an image, and export the edited video (new Library item or a file) or an animated
/// GIF. The original recording is never modified. Blur/overlays are not offered: videos cannot be redacted.
/// </summary>
internal sealed class VideoEditorWindow : Window
{
    private readonly CaptureRecord _record;
    private readonly string _path;
    private readonly AppServices _services;
    private readonly MediaElement _media;
    private readonly Slider _seek;
    private readonly TextBlock _time;
    private readonly TextBlock _plan;
    private readonly TextBlock _status;
    private readonly Button _play;
    private readonly ComboBox _quality;
    private readonly DispatcherTimer _timer;
    private readonly List<Button> _actions = [];
    private VideoEditPlan _edit;
    private long? _sectionStart;
    private bool _playing;
    private bool _busy;

    public VideoEditorWindow(AppServices services, CaptureRecord record, string path)
    {
        _services = services;
        _record = record;
        _path = path;
        _edit = new VideoEditPlan(Math.Max(1, record.DurationMs));
        Title = $"Edit \"{record.Title}\" — SnapForge";
        Width = 1000;
        Height = 700;
        MinWidth = 640;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");

        _media = new MediaElement { Source = new Uri(path), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(_media, "Video preview");
        _media.MediaOpened += (_, _) =>
        {
            if (_media.NaturalDuration.HasTimeSpan)
            {
                long ms = (long)_media.NaturalDuration.TimeSpan.TotalMilliseconds;
                if (ms > 0 && Math.Abs(ms - _edit.DurationMs) > 50 && _edit.IsUnchanged)
                {
                    _edit = new VideoEditPlan(ms);
                }

                _seek!.Maximum = ms;
            }

            UpdateTexts();
        };

        _play = ToolbarButton("Play", "Play or pause (Space)", () => SetPlaying(!_playing));
        _seek = new Slider { Minimum = 0, IsMoveToPointEnabled = true, SmallChange = 1000 / 30.0, LargeChange = 1000, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_seek, "Position");
        _seek.ValueChanged += (_, _) =>
        {
            if (Math.Abs(_seek.Value - _media.Position.TotalMilliseconds) > 40)
            {
                _media.Position = TimeSpan.FromMilliseconds(_seek.Value);
            }

            UpdateTexts();
        };
        _time = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontFamily = new FontFamily("Consolas, Segoe UI"), MinWidth = 150 };

        var transport = new DockPanel { Margin = new Thickness(12, 8, 12, 4) };
        DockPanel.SetDock(_play, Dock.Left);
        transport.Children.Add(_play);
        DockPanel.SetDock(_time, Dock.Right);
        transport.Children.Add(_time);
        transport.Children.Add(_seek);

        var editBar = new WrapPanel { Margin = new Thickness(12, 4, 12, 4) };
        editBar.Children.Add(ToolbarButton("Start here [", "Trim: everything before the current position is cut ([)", () => Apply(() => _edit.SetTrim(Now(), _edit.TrimEndMs))));
        editBar.Children.Add(ToolbarButton("End here ]", "Trim: everything after the current position is cut (])", () => Apply(() => _edit.SetTrim(_edit.TrimStartMs, Now()))));
        editBar.Children.Add(ToolbarButton("Mark section", "Marks where a section to remove begins (M); press again at its end to remove it", MarkSection));
        editBar.Children.Add(ToolbarButton("Undo removal", "Puts back the last removed section", () => Apply(_edit.UndoLastRemoval)));
        editBar.Children.Add(ToolbarButton("Reset", "Keeps the whole video again", () => Apply(_edit.Reset)));
        editBar.Children.Add(ToolbarButton("◀ Frame", "One frame back (,)", () => Step(-1)));
        editBar.Children.Add(ToolbarButton("Frame ▶", "One frame forward (.)", () => Step(1)));

        _quality = new ComboBox { ItemsSource = new[] { "Standard quality", "High quality" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 8, 0), MinWidth = 140 };
        AutomationProperties.SetName(_quality, "Export quality");
        AutomationProperties.SetName(_quality, "Export quality");
        var exportBar = new WrapPanel { Margin = new Thickness(12, 4, 12, 4) };
        exportBar.Children.Add(Action(ToolbarButton("Save frame to Library", "Saves the frame at the current position as an image capture", () => _ = SaveFrameAsync())));
        exportBar.Children.Add(_quality);
        exportBar.Children.Add(Action(ToolbarButton("Save edited video to Library", "Exports the kept parts as a new recording; the original stays unchanged", () => _ = ExportVideoAsync(toLibrary: true))));
        exportBar.Children.Add(Action(ToolbarButton("Export MP4…", "Exports the kept parts to an MP4 file", () => _ = ExportVideoAsync(toLibrary: false))));
        exportBar.Children.Add(Action(ToolbarButton("Export GIF…", "Exports the kept range (trim start to end) as an animated GIF", () => _ = ExportGifAsync())));

        _plan = new TextBlock { Margin = new Thickness(12, 2, 12, 2), TextWrapping = TextWrapping.Wrap };
        _status = new TextBlock { Margin = new Thickness(12, 2, 12, 10), TextWrapping = TextWrapping.Wrap };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        var bottom = new StackPanel();
        bottom.Children.Add(transport);
        bottom.Children.Add(editBar);
        bottom.Children.Add(_plan);
        bottom.Children.Add(exportBar);
        bottom.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(new Border { Background = Brushes.Black, Child = _media });
        Content = root;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) =>
        {
            if (_playing)
            {
                _seek.Value = _media.Position.TotalMilliseconds;
            }
        }, Dispatcher);
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
        Loaded += (_, _) =>
        {
            _media.Play();
            _media.Pause();
            UpdateTexts();
            _status.Text = "Move to a point, then use Start here / End here to trim, or Mark section twice to remove a part. The original recording is never changed.";
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _media.Close();
        };
        PreviewKeyDown += OnKey;
    }

    /// <summary>Test hook: the current edit plan.</summary>
    internal VideoEditPlan Plan => _edit;

    private long Now() => (long)Math.Round(_seek.Value);

    private Button Action(Button b)
    {
        _actions.Add(b);
        return b;
    }

    private static Button ToolbarButton(string text, string tip, Action onClick)
    {
        var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 3, 8, 3) };
        b.SetResourceReference(StyleProperty, "ToolbarButton");
        AutomationProperties.SetName(b, text);
        AutomationProperties.SetHelpText(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    private void Apply(Action change)
    {
        try
        {
            change();
            UpdateTexts();
        }
        catch (ArgumentException ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void MarkSection()
    {
        if (_sectionStart is not { } start)
        {
            _sectionStart = Now();
            _status.Text = $"Section starts at {Fmt(Now())}. Move to where it ends and press Mark section again.";
            return;
        }

        _sectionStart = null;
        Apply(() => _edit.RemoveSection(start, Now()));
        _status.Text = "Section removed from the export.";
    }

    private void Step(int frames)
    {
        SetPlaying(false);
        double frame = 1000.0 / Math.Max(1, _record.FramesPerSecond);
        _seek.Value = Math.Clamp(_seek.Value + (frames * frame), 0, _seek.Maximum);
    }

    private void SetPlaying(bool play)
    {
        _playing = play;
        if (play)
        {
            _media.Play();
            _timer.Start();
        }
        else
        {
            _media.Pause();
            _timer.Stop();
        }

        _play.Content = play ? "Pause" : "Play";
    }

    private void UpdateTexts()
    {
        _time.Text = $"{Fmt(Now())} / {Fmt(_edit.DurationMs)}";
        IReadOnlyList<TimeRangeMs> kept = _edit.KeptRanges;
        _plan.Text = _edit.IsUnchanged
            ? "Keeping the whole video."
            : $"Keeping {kept.Count} part{(kept.Count == 1 ? "" : "s")}: {string.Join(", ", kept.Select(r => $"{Fmt(r.Start)}–{Fmt(r.End)}"))} → {Fmt(_edit.OutputDurationMs)} in total.";
    }

    private static string Fmt(long ms) => TimeSpan.FromMilliseconds(ms).ToString(ms >= 3_600_000 ? @"h\:mm\:ss\.f" : @"m\:ss\.f", CultureInfo.InvariantCulture);

    private void SetBusy(bool busy, string message)
    {
        _busy = busy;
        foreach (Button b in _actions)
        {
            b.IsEnabled = !busy;
        }

        _status.Text = message;
        Cursor = busy ? Cursors.Wait : null;
    }

    private async Task SaveFrameAsync()
    {
        if (_busy)
        {
            return;
        }

        long at = Now();
        SetBusy(true, "Saving the frame…");
        try
        {
            PixelBuffer? frame = await Task.Run(() => VideoAnalysis.DecodeFrame(_path, at)).ConfigureAwait(true);
            if (frame is null)
            {
                SetBusy(false, "That frame could not be read.");
                return;
            }

            DateTime now = DateTime.UtcNow;
            string title = $"{_record.Title} — frame at {Fmt(at)}";
            CaptureRecord rec = await _services.Writer.RunAsync(() => _services.Library.Create(new Core.Storage.CaptureContent(frame, []), frame, now, title)).ConfigureAwait(true);
            SetBusy(false, $"Frame saved to the Library as \"{rec.Title}\" ({frame.Width} × {frame.Height}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NativeCallException)
        {
            SetBusy(false, $"The frame could not be saved: {ex.Message}");
        }
    }

    private async Task ExportVideoAsync(bool toLibrary)
    {
        if (_busy)
        {
            return;
        }

        if (_edit.IsUnchanged)
        {
            _status.Text = "Nothing is cut yet — trim or remove a section first (or use Export in the Library to copy the whole video).";
            return;
        }

        string? target = null;
        Guid libraryId = Guid.Empty;
        if (toLibrary)
        {
            (libraryId, _) = _services.Library.BeginVideo(DateTime.UtcNow);
            target = _services.Library.VideoPath(libraryId);
        }
        else
        {
            var dlg = new SaveFileDialog { Filter = "MP4 video (*.mp4)|*.mp4", FileName = Core.Export.ExportFileNames.Sanitize(_record.Title + " (edited)") + ".mp4", AddExtension = true };
            if (dlg.ShowDialog(this) != true)
            {
                return;
            }

            target = dlg.FileName;
        }

        VideoQuality quality = _quality.SelectedIndex == 1 ? VideoQuality.High : VideoQuality.Standard;
        VideoEditPlan plan = _edit;
        var progress = new Progress<double>(p => _status.Text = $"Exporting… {p:P0}");
        SetBusy(true, "Exporting…");
        try
        {
            await Task.Run(() => VideoExporter.ExportEdited(_path, target, plan, quality, progress, CancellationToken.None)).ConfigureAwait(true);
            if (toLibrary)
            {
                VideoFileInfo info = await Task.Run(() => VideoExporter.Inspect(target)).ConfigureAwait(true);
                PixelBuffer? thumb = await Task.Run(() => VideoAnalysis.DecodeFrame(target, Math.Min(1000, info.DurationMs / 2))).ConfigureAwait(true);
                string title = $"{_record.Title} (edited)";
                CaptureRecord rec = await _services.Writer.RunAsync(() => _services.Library.CompleteVideo(libraryId, new VideoInfo(info.Width, info.Height, info.FramesPerSecond, info.DurationMs, info.HasAudio), thumb, DateTime.UtcNow, title)).ConfigureAwait(true);
                SetBusy(false, $"Saved to the Library as \"{rec.Title}\" ({Fmt(info.DurationMs)}). The original is unchanged.");
            }
            else
            {
                SetBusy(false, $"Exported {Path.GetFileName(target)} ({Fmt(plan.OutputDurationMs)}).");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NativeCallException or ArgumentException)
        {
            if (toLibrary)
            {
                _services.Library.AbandonVideo(libraryId);
            }

            SetBusy(false, $"The video could not be exported: {ex.Message}");
        }
    }

    private async Task ExportGifAsync()
    {
        if (_busy)
        {
            return;
        }

        GifExportSettings? settings = GifExportDialog.Ask(this, _record.Width, _edit.TrimEndMs - _edit.TrimStartMs);
        if (settings is null)
        {
            return;
        }

        var dlg = new SaveFileDialog { Filter = "Animated GIF (*.gif)|*.gif", FileName = Core.Export.ExportFileNames.Sanitize(_record.Title) + ".gif", AddExtension = true };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        string target = dlg.FileName;
        long start = _edit.TrimStartMs, end = _edit.TrimEndMs;
        var progress = new Progress<double>(p => _status.Text = $"Exporting GIF… {p:P0}");
        SetBusy(true, "Exporting GIF…");
        try
        {
            long bytes = await Task.Run(() => GifExport.Write(_path, target, start, end, settings, progress, CancellationToken.None)).ConfigureAwait(true);
            SetBusy(false, $"Exported {Path.GetFileName(target)} ({VideoPlayerWindow.FormatSize(bytes)}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NativeCallException or ArgumentException)
        {
            SetBusy(false, $"The GIF could not be exported: {ex.Message}");
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                SetPlaying(!_playing);
                break;
            case Key.OemComma:
                Step(-1);
                break;
            case Key.OemPeriod:
                Step(1);
                break;
            case Key.OemOpenBrackets:
                Apply(() => _edit.SetTrim(Now(), _edit.TrimEndMs));
                break;
            case Key.OemCloseBrackets:
                Apply(() => _edit.SetTrim(_edit.TrimStartMs, Now()));
                break;
            case Key.M:
                MarkSection();
                break;
            case Key.Escape:
                Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
