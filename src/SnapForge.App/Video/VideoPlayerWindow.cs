using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SnapForge.App.Interop;
using SnapForge.App.Themes;
using SnapForge.Core.Library;

namespace SnapForge.App.Video;

/// <summary>
/// Plays a recording from the Library with the Windows media pipeline (streamed from disk; the file is never loaded
/// into memory). Play/pause (Space), seek bar (←/→ 5 s), time, volume and mute.
/// </summary>
internal sealed class VideoPlayerWindow : Window
{
    private readonly MediaElement _media;
    private readonly Slider _seek;
    private readonly TextBlock _time;
    private readonly Button _play;
    private readonly DispatcherTimer _timer;
    private bool _playing;
    private bool _dragging;

    public VideoPlayerWindow(CaptureRecord record, string path)
    {
        ArgumentNullException.ThrowIfNull(record);
        Record = record;
        Title = $"{record.Title} — SnapForge";
        Width = 960;
        Height = 620;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");

        _media = new MediaElement
        {
            Source = new Uri(path),
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            ScrubbingEnabled = true,
            Stretch = Stretch.Uniform,
        };
        System.Windows.Automation.AutomationProperties.SetName(_media, "Video");
        _media.MediaOpened += (_, _) =>
        {
            _seek!.Maximum = _media.NaturalDuration.HasTimeSpan ? _media.NaturalDuration.TimeSpan.TotalSeconds : 0;
            UpdateTime();
        };
        _media.MediaEnded += (_, _) => SetPlaying(false);
        _media.MediaFailed += (_, e) => MessageBox.Show(this, "This video cannot be played: " + e.ErrorException?.Message, "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);

        _play = new Button { Content = "Play", Width = 72, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(6, 3, 6, 3) };
        System.Windows.Automation.AutomationProperties.SetName(_play, "Play");
        _play.Click += (_, _) => SetPlaying(!_playing);
        _seek = new Slider { Minimum = 0, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true, SmallChange = 1, LargeChange = 5 };
        System.Windows.Automation.AutomationProperties.SetName(_seek, "Position");
        _seek.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _dragging = true));
        _seek.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            _dragging = false;
            _media.Position = TimeSpan.FromSeconds(_seek.Value);
        }));
        _seek.ValueChanged += (_, _) =>
        {
            if (_dragging || Math.Abs(_seek.Value - _media.Position.TotalSeconds) > 0.5)
            {
                _media.Position = TimeSpan.FromSeconds(_seek.Value);
                UpdateTime();
            }
        };
        _time = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), FontFamily = new FontFamily("Consolas, Segoe UI") };
        var mute = new ToggleButton { Content = "Mute", Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 6, 0), IsEnabled = record.HasAudio };
        mute.Click += (_, _) => _media.IsMuted = mute.IsChecked == true;
        var volume = new Slider { Width = 90, Minimum = 0, Maximum = 1, Value = 0.8, VerticalAlignment = VerticalAlignment.Center, IsEnabled = record.HasAudio };
        System.Windows.Automation.AutomationProperties.SetName(volume, "Volume");
        _media.Volume = volume.Value;
        volume.ValueChanged += (_, _) => _media.Volume = volume.Value;

        var bar = new DockPanel { Margin = new Thickness(10, 6, 10, 8) };
        DockPanel.SetDock(_play, Dock.Left);
        bar.Children.Add(_play);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_time);
        right.Children.Add(mute);
        right.Children.Add(volume);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(_seek);

        var info = new TextBlock
        {
            Margin = new Thickness(12, 0, 12, 6),
            FontSize = 12,
            Text = $"{record.Width}×{record.Height} · {record.FramesPerSecond} FPS · {(record.HasAudio ? "with sound" : "no sound")} · {FormatSize(record.SizeBytes)}"
                + (record.Recovered ? " · recovered after an interruption" : string.Empty),
        };
        info.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");

        var root = new DockPanel();
        DockPanel.SetDock(info, Dock.Bottom);
        root.Children.Add(info);
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(new Border { Background = Brushes.Black, Child = _media });
        Content = root;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, (_, _) =>
        {
            if (!_dragging)
            {
                _seek.Value = _media.Position.TotalSeconds;
            }

            UpdateTime();
        }, Dispatcher);
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
        Loaded += (_, _) =>
        {
            _media.Play();
            _media.Pause(); // shows the first frame
            SetPlaying(true);
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _media.Close();
        };
        PreviewKeyDown += OnKey;
    }

    public CaptureRecord Record { get; }

    public bool IsPlaying => _playing;

    private void SetPlaying(bool play)
    {
        _playing = play;
        if (play)
        {
            if (_media.NaturalDuration.HasTimeSpan && _media.Position >= _media.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(100))
            {
                _media.Position = TimeSpan.Zero;
            }

            _media.Play();
            _timer.Start();
        }
        else
        {
            _media.Pause();
        }

        _play.Content = play ? "Pause" : "Play";
        System.Windows.Automation.AutomationProperties.SetName(_play, play ? "Pause" : "Play");
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                SetPlaying(!_playing);
                e.Handled = true;
                break;
            case Key.Left:
                _media.Position = TimeSpan.FromSeconds(Math.Max(0, _media.Position.TotalSeconds - 5));
                e.Handled = true;
                break;
            case Key.Right:
                _media.Position += TimeSpan.FromSeconds(5);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void UpdateTime()
    {
        TimeSpan total = _media.NaturalDuration.HasTimeSpan ? _media.NaturalDuration.TimeSpan : TimeSpan.FromMilliseconds(Record.DurationMs);
        _time.Text = $"{Format(_media.Position)} / {Format(total)}";
    }

    internal static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    internal static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
