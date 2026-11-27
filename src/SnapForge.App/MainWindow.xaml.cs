using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapForge.App.Capture;
using SnapForge.App.Dialogs;
using SnapForge.App.Interop;
using SnapForge.App.Rendering;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.App.Views;
using SnapForge.Core;
using SnapForge.Core.Annotations;
using SnapForge.Core.Capture;
using SnapForge.Core.Editing;
using SnapForge.Core.Export;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using SnapForge.Core.Storage;
using CaptureMode = SnapForge.Core.Capture.CaptureMode;

namespace SnapForge.App;

/// <summary>
/// The main window: Editor and Library workspaces, menu, Recent Captures Tray and status bar.
/// Captures are library-backed sessions that are saved automatically (debounced, in the background, crash-resistant);
/// captures "without history" live only in memory.
/// </summary>
public partial class MainWindow : Window
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 8;
    private const int MaxCachedSessions = 6;

    private readonly AppServices _services;
    private readonly Dictionary<Guid, EditorSession> _sessions = [];
    private readonly LinkedList<Guid> _sessionOrder = new();
    private readonly ObservableCollection<TrayTileViewModel> _trayTiles = [];
    private readonly DispatcherTimer _autosaveTimer;
    private EditorSession? _current;
    private string? _lastExportDirectory;
    private bool _fitMode = true;
    private bool _exiting;
    private bool _exportedSinceLoad;
    private bool _suppressUiEvents;

    public MainWindow()
        : this(AppServices.Current ?? throw new InvalidOperationException("AppServices.Current must be set before creating the main window."))
    {
    }

    internal MainWindow(AppServices services)
    {
        _services = services;
        _suppressUiEvents = true;
        InitializeComponent();
        _suppressUiEvents = false;
        Title = ProductInfo.Name;
        TrayItems.ItemsSource = _trayTiles;
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _autosaveTimer.Tick += async (_, _) =>
        {
            _autosaveTimer.Stop();
            if (_current is not null)
            {
                await SaveSessionAsync(_current).ConfigureAwait(true);
            }
        };

        Editor.StateChanged += (_, _) => UpdateCommandState();
        SourceInitialized += (_, _) =>
        {
            WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
            WindowChrome.DisableTransitions(this);
        };
        Loaded += (_, _) =>
        {
            DescribeDisplays();
            RefreshTray();
        };
        _services.Tray.Changed += (_, _) => Dispatcher.BeginInvoke(RefreshTray);
        _services.Library.Changed += OnLibraryChanged;
        _services.LibraryReplaced += (_, _) => Dispatcher.Invoke(() =>
        {
            _services.Library.Changed += OnLibraryChanged;
            _services.Tray.Changed += (_, _) => Dispatcher.BeginInvoke(RefreshTray);
            LibraryPane.Refresh();
            RefreshTray();
        });
        _services.SettingsChanged += (_, _) => Dispatcher.BeginInvoke(ApplySettingsToUi);
        ThemeManager.ThemeChanged += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);

        LibraryPane.Initialize(this, _services);
        ApplySettingsToUi();
        RefreshQuickStyles();
        UpdateCommandState();
    }

    internal EditorDocument? Document => _current?.Document;

    internal EditorSession? CurrentSession => _current;

    internal AppServices Services => _services;

    /// <summary>Messages for the user that may come from background work (e.g. failed hotkeys) end up here.</summary>
    internal void SetStatus(string message) => StatusText.Text = message;

    // ------------------------------------------------------------------ capture intake

    /// <summary>Loads an image as a new capture using the current capture options (also used by tests).</summary>
    internal EditorSession LoadImage(PixelBuffer image, DateTime capturedAt) =>
        AcceptCapture(image, capturedAt, _services.Settings.Capture with { PreviewInEditor = true, CopyToClipboard = false });

    internal EditorSession AcceptCapture(PixelBuffer image, DateTime capturedAt, CaptureOptions options)
    {
        var document = new EditorDocument(image);
        var session = new EditorSession(document, capturedAt, CaptureLibrary.DefaultTitle(capturedAt.ToUniversalTime()), null);
        Register(session);

        if (options.SaveToLibrary)
        {
            session.Creation = CreateInLibraryAsync(session);
        }

        if (options.CopyToClipboard)
        {
            _ = CopyImageAsync(image, "Captured and copied to the clipboard.");
        }

        bool preview = options.PreviewInEditor || (!options.SaveToLibrary && !options.CopyToClipboard);
        if (preview)
        {
            ShowSession(session);
            ShowEditorWorkspace();
            BringToFront();
        }

        RefreshTray();
        return session;
    }

    private async Task CreateInLibraryAsync(EditorSession session)
    {
        session.IsSaving = true;
        UpdateSaveState();
        try
        {
            EditorDocument doc = session.Document;
            long state = doc.History.CurrentStateId;
            PixelBuffer flattened = ImageExporter.Flatten(doc.Image, doc.Annotations);
            Annotation[] annotations = [.. doc.Annotations];
            PixelBuffer baseImage = doc.BaseImage;
            DateTime created = session.CapturedAt.ToUniversalTime();
            CaptureRecord record = await _services.Writer.RunAsync(() =>
                _services.Library.Create(new CaptureContent(baseImage, annotations), flattened, created)).ConfigureAwait(true);

            Guid oldKey = session.Key;
            session.CaptureId = record.Id;
            session.Key = record.Id;
            session.Title = record.Title;
            ReKey(oldKey, session);
            doc.MarkSaved(state);
            session.SavedBaseVersion = session.BaseVersion;
            session.LastSaveError = null;
            _services.Tray.Add(record.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            session.LastSaveError = $"Could not save to the Library: {ex.Message}";
        }
        finally
        {
            session.IsSaving = false;
            UpdateSaveState();
            RefreshTray();
            if (session.Document.IsDirty && session.InLibrary && ReferenceEquals(session, _current))
            {
                _autosaveTimer.Start();
            }
        }
    }

    /// <summary>Saves a session's current state to the Library (background, crash-resistant). Safe to call often.</summary>
    internal async Task SaveSessionAsync(EditorSession session)
    {
        if (session.Creation is { IsCompleted: false } creation)
        {
            await creation.ConfigureAwait(true);
        }

        if (!session.InLibrary || session.Document.IsDiscarded || (!session.Document.IsDirty && session.SavedBaseVersion == session.BaseVersion))
        {
            return;
        }

        EditorDocument doc = session.Document;
        long state = doc.History.CurrentStateId;
        int baseVersion = session.BaseVersion;
        PixelBuffer flattened = ImageExporter.Flatten(doc.Image, doc.Annotations);
        Annotation[] annotations = [.. doc.Annotations];
        PixelBuffer? newBase = baseVersion != session.SavedBaseVersion ? doc.BaseImage.Clone() : null;
        bool reversible = doc.HasReversibleRedactions;
        Guid id = session.CaptureId!.Value;
        session.IsSaving = true;
        UpdateSaveState();
        try
        {
            await _services.Writer.RunAsync(() => _services.Library.SaveContent(id, newBase, annotations, flattened, reversible)).ConfigureAwait(true);
            if (!doc.IsDiscarded)
            {
                doc.MarkSaved(state);
            }

            session.SavedBaseVersion = baseVersion;
            session.LastSaveError = null;
            _services.Thumbnails.Invalidate(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or KeyNotFoundException)
        {
            // e.g. disk full: the edits stay open in memory and saving is retried on the next change.
            session.LastSaveError = $"Could not save changes to the Library: {ex.Message} Your edits are still open; export them or free disk space.";
        }
        finally
        {
            session.IsSaving = false;
            UpdateSaveState();
            RefreshTray();
        }
    }

    internal async Task FlushAllAsync()
    {
        _autosaveTimer.Stop();
        Editor.CommitTextEdit();
        foreach (EditorSession s in _sessions.Values.ToList())
        {
            await SaveSessionAsync(s).ConfigureAwait(true);
        }

        await _services.Writer.WhenIdleAsync().ConfigureAwait(true);
    }

    // ------------------------------------------------------------------ sessions

    /// <summary>Opens a library capture in the Editor (re-uses an open session if there is one).</summary>
    internal async Task OpenCaptureAsync(Guid id)
    {
        Editor.CommitTextEdit();
        if (_sessions.TryGetValue(id, out EditorSession? open))
        {
            ShowSession(open);
            ShowEditorWorkspace();
            _services.Tray.Add(id);
            return;
        }

        CaptureRecord? record = _services.Library.Find(id);
        if (record is { IsVideo: true })
        {
            App.ShowVideo(id);
            _services.Tray.Add(id);
            return;
        }

        if (record is null)
        {
            SetStatus("That capture no longer exists.");
            return;
        }

        if (record.Damage is not null)
        {
            MessageBox.Show(this, $"\"{record.Title}\" cannot be opened because it is damaged:\n\n{record.Damage}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SetStatus($"Opening {record.Title}…");
            CaptureContent content = await _services.Writer.RunAsync(() => _services.Library.Load(id)).ConfigureAwait(true);
            var session = new EditorSession(new EditorDocument(content.BaseImage, content.Annotations), record.CreatedUtc.ToLocalTime(), record.Title, id);
            Register(session);
            ShowSession(session);
            ShowEditorWorkspace();
            _services.Tray.Add(id);
            _ = _services.Writer.RunAsync(() => _services.Library.MarkOpened(id, DateTime.UtcNow));
            SetStatus($"Opened {record.Title}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or KeyNotFoundException)
        {
            MessageBox.Show(this, $"\"{record.Title}\" could not be opened.\n\n{ex.Message}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Register(EditorSession session)
    {
        _sessions[session.Key] = session;
        _sessionOrder.Remove(session.Key);
        _sessionOrder.AddFirst(session.Key);
        session.Document.Changed += (_, e) => OnSessionDocumentChanged(session, e);
        TrimSessionCache();
    }

    private void ReKey(Guid oldKey, EditorSession session)
    {
        if (oldKey == session.Key)
        {
            return;
        }

        _sessions.Remove(oldKey);
        _sessions[session.Key] = session;
        LinkedListNode<Guid>? node = _sessionOrder.Find(oldKey);
        if (node is not null)
        {
            node.Value = session.Key;
        }
    }

    /// <summary>Keeps memory bounded: saved, inactive library sessions beyond the limit are discarded (reloaded on demand).</summary>
    private void TrimSessionCache()
    {
        foreach (Guid key in _sessionOrder.Reverse().ToList())
        {
            if (_sessions.Count <= MaxCachedSessions)
            {
                break;
            }

            if (_sessions.TryGetValue(key, out EditorSession? s) && !ReferenceEquals(s, _current) && s.InLibrary && !s.Document.IsDirty && !s.IsSaving
                && s.SavedBaseVersion == s.BaseVersion && s.Creation is null or { IsCompleted: true })
            {
                RemoveSession(s);
            }
        }
    }

    private void RemoveSession(EditorSession s)
    {
        _sessions.Remove(s.Key);
        _sessionOrder.Remove(s.Key);
        if (!s.IsSaving)
        {
            s.Document.Discard();
        }
    }

    private void ShowSession(EditorSession session)
    {
        if (_current is not null && !ReferenceEquals(_current, session))
        {
            _ = SaveSessionAsync(_current);
        }

        _current = session;
        _exportedSinceLoad = false;
        _sessionOrder.Remove(session.Key);
        _sessionOrder.AddFirst(session.Key);
        Editor.Load(session.Document);
        StartScreen.Visibility = Visibility.Collapsed;
        Viewport.Visibility = Visibility.Visible;
        ToolPalette.IsEnabled = true;
        PropertiesContent.IsEnabled = true;
        _fitMode = true;
        Dispatcher.BeginInvoke(ApplyFitZoom, DispatcherPriority.Loaded);
        UpdateCommandState();
        UpdateSaveState();
        RefreshTray();
        Editor.Focus();
    }

    private void ShowNoSession()
    {
        _current = null;
        Editor.Unload();
        StartScreen.Visibility = Visibility.Visible;
        Viewport.Visibility = Visibility.Collapsed;
        ToolPalette.IsEnabled = false;
        PropertiesContent.IsEnabled = false;
        ImageInfo.Text = string.Empty;
        UpdateCommandState();
        UpdateSaveState();
        RefreshTray();
    }

    private void OnSessionDocumentChanged(EditorSession session, DocumentChangedEventArgs e)
    {
        if (e.SizeChanged)
        {
            session.BaseVersion++; // crop (or its undo) changed the base pixels
        }

        if (e.ImageChanged && _exportedSinceLoad && ReferenceEquals(session, _current))
        {
            SetStatus("Redaction changed. Anything you already copied or exported still contains the earlier version.");
        }

        if (session.InLibrary || session.Creation is not null)
        {
            if (ReferenceEquals(session, _current))
            {
                _autosaveTimer.Stop();
                _autosaveTimer.Start();
            }
            else
            {
                _ = SaveSessionAsync(session);
            }
        }

        if (ReferenceEquals(session, _current))
        {
            ImageInfo.Text = $"{session.Document.Image.Width} × {session.Document.Image.Height} px";
            if (e.SizeChanged)
            {
                Dispatcher.BeginInvoke(ApplyFitZoom, DispatcherPriority.Loaded);
            }
        }

        UpdateSaveState();
        UpdateTrayTileState();
    }

    private async Task CloseSessionAsync(EditorSession session, bool removeFromTray)
    {
        if (!session.InLibrary && session.Creation is null && session.Document.IsDirty &&
            MessageBox.Show(this, $"\"{session.Title}\" was captured without history and has not been exported.\n\nClose it and lose it?", ProductInfo.Name,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!session.InLibrary && session.Creation is null && !session.Document.IsDirty && !_exportedSinceLoad && ReferenceEquals(session, _current) &&
            MessageBox.Show(this, $"\"{session.Title}\" is not in the Library. Close it?", ProductInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await SaveSessionAsync(session).ConfigureAwait(true);
        if (removeFromTray && session.CaptureId is { } id)
        {
            _services.Tray.Close(id);
        }

        bool wasCurrent = ReferenceEquals(session, _current);
        if (wasCurrent)
        {
            ShowNoSession();
        }

        RemoveSession(session);
        RefreshTray();
    }

    // ------------------------------------------------------------------ library notifications

    private void OnLibraryChanged(object? sender, LibraryChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        foreach (Guid id in e.Ids)
        {
            CaptureRecord? r = _services.Library.Find(id);
            if (_sessions.TryGetValue(id, out EditorSession? s) && r is not null)
            {
                s.Title = r.Title;
            }

            _services.Thumbnails.Invalidate(id);
        }

        LibraryPane.OnLibraryChanged(e.Ids);
        RefreshTray();
        UpdateSaveState();
    });

    /// <summary>Called by the Library view after captures were moved to the Trash or deleted.</summary>
    internal void OnCapturesRemoved(IEnumerable<Guid> ids)
    {
        foreach (Guid id in ids)
        {
            _services.Tray.Close(id);
            if (_sessions.TryGetValue(id, out EditorSession? s))
            {
                if (ReferenceEquals(s, _current))
                {
                    ShowNoSession();
                }

                RemoveSession(s);
            }
        }

        RefreshTray();
    }

    // ------------------------------------------------------------------ tray

    private void RefreshTray()
    {
        var wanted = new List<(Guid Key, bool Pinned, bool NotInLibrary)>();
        foreach (EditorSession s in _sessions.Values.Where(s => !s.InLibrary).OrderByDescending(s => s.CapturedAt))
        {
            wanted.Add((s.Key, false, true));
        }

        foreach (TrayItem item in _services.Tray.Items)
        {
            if (_services.Library.Find(item.Id) is { IsDeleted: false })
            {
                wanted.Add((item.Id, item.Pinned, false));
            }
        }

        // Rebuild only if the set or order changed; otherwise update in place (keeps thumbnails, avoids flicker).
        if (!wanted.Select(w => w.Key).SequenceEqual(_trayTiles.Select(t => t.Key)) || wanted.Zip(_trayTiles).Any(p => p.First.NotInLibrary != p.Second.NotInLibrary))
        {
            _trayTiles.Clear();
            foreach ((Guid key, bool pinned, bool notInLibrary) in wanted)
            {
                _trayTiles.Add(new TrayTileViewModel { Key = key, NotInLibrary = notInLibrary, Pinned = pinned });
            }
        }

        foreach ((TrayTileViewModel tile, (Guid key, bool pinned, bool _)) in _trayTiles.Zip(wanted))
        {
            tile.Pinned = pinned;
            CaptureRecord? record = _services.Library.Find(key);
            tile.Title = record?.Title ?? (_sessions.TryGetValue(key, out EditorSession? s) ? s.Title : "Capture");
            tile.IsVideo = record?.IsVideo == true;
            tile.DurationText = record is { IsVideo: true } ? Video.VideoPlayerWindow.Format(TimeSpan.FromMilliseconds(record.DurationMs)) : string.Empty;
            tile.Detail = record is null
                ? string.Empty
                : record.IsVideo
                    ? $"Video · {tile.DurationText} · {record.CreatedUtc.ToLocalTime():g} · {record.Width}×{record.Height} · {Video.VideoPlayerWindow.FormatSize(record.SizeBytes)}"
                    : $"{record.CreatedUtc.ToLocalTime():g} · {record.Width}×{record.Height}";
            _ = LoadTrayThumbnailAsync(tile);
        }

        UpdateTrayTileState();
        TrayEmptyText.Visibility = _trayTiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TrayHeader.Text = _trayTiles.Count == 0 ? "RECENT CAPTURES" : $"RECENT CAPTURES ({_trayTiles.Count})";
    }

    private void UpdateTrayTileState()
    {
        foreach (TrayTileViewModel tile in _trayTiles)
        {
            tile.IsCurrent = _current is not null && tile.Key == _current.Key;
            tile.IsUnsaved = _sessions.TryGetValue(tile.Key, out EditorSession? s) && (s.Document.IsDirty || s.IsSaving || s.LastSaveError is not null) && s.InLibrary;
        }
    }

    private async Task LoadTrayThumbnailAsync(TrayTileViewModel tile)
    {
        if (tile.NotInLibrary)
        {
            if (_sessions.TryGetValue(tile.Key, out EditorSession? s) && !s.Document.IsDiscarded)
            {
                tile.Thumbnail = BitmapConversion.ToFrozenBitmap(Thumbnailer.Create(s.Document.Image, 160));
            }

            return;
        }

        tile.Thumbnail = await _services.Thumbnails.GetAsync(_services.Library, tile.Key).ConfigureAwait(true) ?? tile.Thumbnail;
    }

    private static TrayTileViewModel? TileFrom(object sender) => (sender as FrameworkElement)?.DataContext as TrayTileViewModel;

    private async void OnTrayTileClick(object sender, RoutedEventArgs e)
    {
        if (TileFrom(sender) is { } tile)
        {
            await OnTrayTileClickedAsync(tile).ConfigureAwait(true);
        }
    }

    private async void OnTrayOpen(object sender, RoutedEventArgs e)
    {
        if (TileFrom(sender) is { } tile)
        {
            await OpenTileAsync(tile).ConfigureAwait(true);
        }
    }

    private async Task OpenTileAsync(TrayTileViewModel tile)
    {
        if (_sessions.TryGetValue(tile.Key, out EditorSession? s))
        {
            ShowSession(s);
            ShowEditorWorkspace();
        }
        else
        {
            await OpenCaptureAsync(tile.Key).ConfigureAwait(true);
        }
    }

    private void OnTrayTogglePin(object sender, RoutedEventArgs e)
    {
        if (TileFrom(sender) is { NotInLibrary: false } tile)
        {
            _services.Tray.SetPinned(tile.Key, !tile.Pinned);
        }
        else
        {
            SetStatus("Save the capture to the Library before pinning it.");
        }
    }

    private async void OnTrayClose(object sender, RoutedEventArgs e)
    {
        if (TileFrom(sender) is not { } tile)
        {
            return;
        }

        if (_sessions.TryGetValue(tile.Key, out EditorSession? s))
        {
            await CloseSessionAsync(s, removeFromTray: true).ConfigureAwait(true);
        }
        else
        {
            _services.Tray.Close(tile.Key);
        }
    }

    private void OnTrayCloseUnpinned(object sender, RoutedEventArgs e)
    {
        _services.Tray.CloseAllUnpinned();
        SetStatus("Closed unpinned thumbnails. The captures are still in the Library.");
    }

    private void OnTrayShowInLibrary(object sender, RoutedEventArgs e)
    {
        if (TileFrom(sender) is { NotInLibrary: false } tile)
        {
            ShowLibraryWorkspace();
            LibraryPane.Reveal(tile.Key);
        }
    }

    private async void OnTrayDelete(object sender, RoutedEventArgs e)
    {
        if (TileFrom(sender) is not { } tile)
        {
            return;
        }

        if (tile.NotInLibrary)
        {
            if (_sessions.TryGetValue(tile.Key, out EditorSession? s))
            {
                await CloseSessionAsync(s, removeFromTray: true).ConfigureAwait(true);
            }

            return;
        }

        if (MessageBox.Show(this, $"Move \"{tile.Title}\" to the Library's Trash?\n\nYou can restore it from Library → Trash until the Trash is emptied.", ProductInfo.Name,
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await DeleteCapturesAsync([tile.Key]).ConfigureAwait(true);
        }
    }

    internal async Task DeleteCapturesAsync(IReadOnlyCollection<Guid> ids)
    {
        foreach (Guid id in ids)
        {
            if (_sessions.TryGetValue(id, out EditorSession? s))
            {
                await SaveSessionAsync(s).ConfigureAwait(true);
            }
        }

        await _services.Writer.RunAsync(() => _services.Library.MoveToTrash(ids, DateTime.UtcNow)).ConfigureAwait(true);
        OnCapturesRemoved(ids);
        SetStatus(ids.Count == 1 ? "Moved to Trash." : $"Moved {ids.Count} captures to Trash.");
    }

    private void OnTrayMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TrayScroller.ScrollToHorizontalOffset(TrayScroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ workspaces

    internal void ShowEditorWorkspace()
    {
        EditorWorkspace.Visibility = Visibility.Visible;
        LibraryPane.Visibility = Visibility.Collapsed;
        EditorStatusPanel.Visibility = Visibility.Visible;
        _suppressUiEvents = true;
        EditorTab.IsChecked = true;
        _suppressUiEvents = false;
    }

    internal void ShowLibraryWorkspace()
    {
        Editor.CommitTextEdit();
        if (_current is not null)
        {
            _ = SaveSessionAsync(_current);
        }

        EditorWorkspace.Visibility = Visibility.Collapsed;
        LibraryPane.Visibility = Visibility.Visible;
        EditorStatusPanel.Visibility = Visibility.Collapsed;
        SetStatus("Library: double-click or Enter opens a capture · F2 rename · Del moves to Trash · right-click for more.");
        _suppressUiEvents = true;
        LibraryTab.IsChecked = true;
        _suppressUiEvents = false;
        LibraryPane.Refresh();
        LibraryPane.FocusGrid();
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        if (ReferenceEquals(sender, LibraryTab))
        {
            ShowLibraryWorkspace();
        }
        else
        {
            ShowEditorWorkspace();
        }
    }

    private void OnShowEditor(object sender, RoutedEventArgs e) => ShowEditorWorkspace();

    private void OnShowLibrary(object sender, RoutedEventArgs e) => ShowLibraryWorkspace();

    internal void BringToFront()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    // ------------------------------------------------------------------ capture commands

    private void OnNewCapture(object sender, RoutedEventArgs e) => _ = App.CaptureWithOptionsAsync(_services.Settings.Capture);

    private void OnCaptureRegion(object sender, RoutedEventArgs e) => _ = App.CaptureWithOptionsAsync(_services.Settings.Capture with { Mode = CaptureMode.Region });

    private void OnCaptureWindow(object sender, RoutedEventArgs e) => _ = App.CaptureWithOptionsAsync(_services.Settings.Capture with { Mode = CaptureMode.Window });

    private void OnCaptureDisplay(object sender, RoutedEventArgs e) => _ = App.CaptureWithOptionsAsync(_services.Settings.Capture with { Mode = CaptureMode.SingleScreen });

    private void OnCaptureActiveWindow(object sender, RoutedEventArgs e) => _ = App.CaptureWithOptionsAsync(_services.Settings.Capture with { Mode = CaptureMode.ActiveWindow });

    private void OnRepeatLastCapture(object sender, RoutedEventArgs e) => _ = App.RepeatLastCaptureAsync();

    /// <summary>Opens the clipboard's image as a new capture (saved to the Library like any capture).</summary>
    private void OnNewFromClipboard(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                SetStatus("The clipboard does not contain an image.");
                return;
            }

            System.Windows.Media.Imaging.BitmapSource? bitmap = Clipboard.GetImage();
            if (bitmap is null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                SetStatus("The clipboard image could not be read.");
                return;
            }

            PixelBuffer image = BitmapConversion.FromBitmapSource(bitmap);
            image.ForceOpaque();
            AcceptCapture(image, DateTime.Now, _services.Settings.Capture with { PreviewInEditor = true, CopyToClipboard = false });
            ShowEditorWorkspace();
            SetStatus($"Opened the clipboard image ({image.Width} × {image.Height}).");
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            SetStatus($"The clipboard could not be read: {ex.Message}");
        }
    }

    private void OnCaptureAll(object sender, RoutedEventArgs e) => _ = App.CaptureWithOptionsAsync(_services.Settings.Capture with { Mode = CaptureMode.AllScreens });

    private void OnShowCaptureWindow(object sender, RoutedEventArgs e) => App.ShowCaptureWindow();

    private void OnPresetMenu(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: CapturePreset preset })
        {
            _ = App.CaptureWithOptionsAsync(preset.Options);
        }
    }

    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        bool has = _current is not null;
        SaveToLibraryMenu.IsEnabled = has && !_current!.InLibrary && _current.Creation is null;
        ExportMenu.IsEnabled = CopyMenu.IsEnabled = has;
        ExportPackageMenu.IsEnabled = has && _current!.InLibrary;
        SanitizeMenu.IsEnabled = has;
        CloseCaptureMenu.IsEnabled = has;
        UndoMenu.IsEnabled = has && _current!.Document.History.CanUndo;
        RedoMenu.IsEnabled = has && _current!.Document.History.CanRedo;
        DeleteMenu.IsEnabled = Editor.HasSelection;
        ShowTrayMenu.IsChecked = _services.Settings.ShowRecentTray;
        ShowPanelMenu.IsChecked = _services.Settings.ShowPropertiesPanel;
        ThemeSystemMenu.IsChecked = _services.Settings.Theme == ThemePreference.System;
        ThemeLightMenu.IsChecked = _services.Settings.Theme == ThemePreference.Light;
        ThemeDarkMenu.IsChecked = _services.Settings.Theme == ThemePreference.Dark;
        PresetsMenu.Items.Clear();
        foreach (CapturePreset p in _services.Settings.Presets)
        {
            var item = new MenuItem { Header = p.Name, Tag = p, InputGestureText = p.Hotkey?.ToString() ?? string.Empty };
            item.Click += OnPresetMenu;
            PresetsMenu.Items.Add(item);
        }
    }

    // ------------------------------------------------------------------ editing

    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents || sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse(tag, out AnnotationTool tool) || Editor is null)
        {
            return;
        }

        Editor.SetTool(tool);
        UpdateToolProperties();
        SetStatus(ToolHint(tool));
    }

    private static string ToolHint(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Select => "Click an annotation to select it; drag to move. Double-click text to edit. Delete removes it.",
        AnnotationTool.Text => "Click to place text. Enter to finish, Shift+Enter for a new line, Esc to cancel.",
        AnnotationTool.StepMarker => "Click to place the next numbered step.",
        AnnotationTool.Highlight => "Drag over content to highlight it.",
        AnnotationTool.Crop => "Drag the area to keep. The rest is removed (Undo restores it while editing).",
        AnnotationTool.Redact => "Drag over sensitive content. Exports contain only the black box; the editable Library copy keeps originals until you sanitize.",
        _ => "Drag to draw. Hold Shift to constrain.",
    };

    private void UpdateToolProperties()
    {
        AnnotationTool tool = Editor.Tool;
        ToolNameText.Text = tool switch
        {
            AnnotationTool.StepMarker => "Step",
            AnnotationTool.Rectangle => "Rectangle",
            _ => tool.ToString(),
        };
        ToolHintText.Text = ToolHint(tool);
        StyleProperties.Visibility = tool is AnnotationTool.Redact or AnnotationTool.Crop ? Visibility.Collapsed : Visibility.Visible;
        ThicknessLabel.Text = tool switch
        {
            AnnotationTool.Text => "Size (text size follows thickness)",
            AnnotationTool.StepMarker => "Size",
            AnnotationTool.Highlight => "Thickness (not used by highlight)",
            _ => "Thickness",
        };
        Annotation? selected = Editor.Selected;
        SelectionText.Text = selected is null ? string.Empty : $"Selected {selected.GetType().Name.Replace("Annotation", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}: changing color or thickness applies to it (one undo step each).";
    }

    private void OnColorChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents || sender is not FrameworkElement { Tag: string hex } || Editor is null)
        {
            return;
        }

        ApplyColor(ArgbColor.Parse(hex));
    }

    private void ApplyColor(ArgbColor color)
    {
        Editor.SetColor(color);
        HexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _services.UpdateSettings(s => s with { LastColor = color });
    }

    private void OnHexCommitted(object sender, RoutedEventArgs e) => CommitHex();

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
    }

    private void CommitHex()
    {
        try
        {
            ArgbColor c = ArgbColor.Parse(HexBox.Text.Trim());
            ApplyColor(c with { A = 255 });
            foreach (RadioButton rb in SwatchPanel.Children.OfType<RadioButton>())
            {
                _suppressUiEvents = true;
                rb.IsChecked = string.Equals((string)rb.Tag, HexBox.Text, StringComparison.OrdinalIgnoreCase);
                _suppressUiEvents = false;
            }
        }
        catch (FormatException)
        {
            SetStatus("Enter a color as #RRGGBB, for example #E53935.");
        }
        catch (OverflowException)
        {
            SetStatus("Enter a color as #RRGGBB, for example #E53935.");
        }
    }

    private void OnThicknessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ThicknessValue is null)
        {
            return;
        }

        ThicknessValue.Text = $"{e.NewValue:0} px";
        if (_suppressUiEvents || Editor is null)
        {
            return;
        }

        Editor.SetStrokeWidth(e.NewValue);
        _services.UpdateSettings(s => s with { LastStrokeWidth = e.NewValue });
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();

    private void OnRedo(object sender, RoutedEventArgs e) => Redo();

    private void OnDeleteSelection(object sender, RoutedEventArgs e) => Editor.DeleteSelection();

    private void OnCropTool(object sender, RoutedEventArgs e) => CropTool.IsChecked = true;

    private void OnRedactTool(object sender, RoutedEventArgs e) => RedactTool.IsChecked = true;

    private void Undo()
    {
        Editor.CancelGesture();
        Editor.CommitTextEdit();
        string? what = _current?.Document.History.UndoDescription;
        if (_current?.Document.Undo() == true)
        {
            SetStatus($"Undid: {what}");
        }
    }

    private void Redo()
    {
        Editor.CancelGesture();
        Editor.CommitTextEdit();
        string? what = _current?.Document.History.RedoDescription;
        if (_current?.Document.Redo() == true)
        {
            SetStatus($"Redid: {what}");
        }
    }

    private void UpdateCommandState()
    {
        bool hasDoc = _current is not null;
        EditorDocument? doc = _current?.Document;
        UndoButton.IsEnabled = doc?.History.CanUndo == true;
        RedoButton.IsEnabled = doc?.History.CanRedo == true;
        UndoButton.ToolTip = doc?.History.UndoDescription is { } u ? $"Undo {u} (Ctrl+Z)" : "Undo (Ctrl+Z)";
        RedoButton.ToolTip = doc?.History.RedoDescription is { } r ? $"Redo {r} (Ctrl+Y)" : "Redo (Ctrl+Y)";
        CopyButton.IsEnabled = hasDoc;
        DragOutButton.IsEnabled = hasDoc;
        SaveButton.IsEnabled = hasDoc;
        DeleteButton.IsEnabled = Editor.HasSelection;
        if (hasDoc)
        {
            ImageInfo.Text = $"{doc!.Image.Width} × {doc.Image.Height} px";
        }

        Title = _current is null ? ProductInfo.Name : $"{_current.Title} – {ProductInfo.Name}";
        UpdateToolProperties();
        UpdateSaveState();
    }

    private void UpdateSaveState()
    {
        EditorSession? s = _current;
        RedactionBanner.Visibility = s is { InLibrary: true } && s.Document.HasReversibleRedactions ? Visibility.Visible : Visibility.Collapsed;
        NoHistoryBanner.Visibility = s is { InLibrary: false, Creation: null } ? Visibility.Visible : Visibility.Collapsed;
        SaveErrorBanner.Visibility = s?.LastSaveError is not null ? Visibility.Visible : Visibility.Collapsed;
        SaveErrorText.Text = s?.LastSaveError ?? string.Empty;
        SaveStateText.Text = s switch
        {
            null => string.Empty,
            { LastSaveError: not null } => "⚠ Not saved",
            { IsSaving: true } => "Saving to Library…",
            { InLibrary: false, Creation: null } => "Not in Library",
            { InLibrary: true } when s.Document.IsDirty => "Unsaved changes",
            { InLibrary: true } => "Saved to Library",
            _ => "Saving to Library…",
        };
        if (s is not null)
        {
            CaptureInfoText.Text = s.InLibrary
                ? $"{s.Title}\nCaptured {s.CapturedAt:g}\nStored in the Library (saved automatically).{(s.Document.HasReversibleRedactions ? "\nLibrary copy keeps original pixels under redactions until sanitized." : string.Empty)}"
                : $"{s.Title}\nCaptured {s.CapturedAt:g}\nNot in the Library (captured without history).";
        }
        else
        {
            CaptureInfoText.Text = string.Empty;
        }
    }

    // ------------------------------------------------------------------ quick styles

    private void RefreshQuickStyles()
    {
        AppSettings s = _services.Settings;
        var favorites = s.FavoriteQuickStyles.ToHashSet();
        var all = s.UserQuickStyles.Concat(QuickStyle.BuiltIns)
            .Select(q => new QuickStyleViewModel(q, favorites.Contains(q.Id)))
            .OrderByDescending(v => v.Favorite)
            .ToList();
        QuickStylesList.ItemsSource = all;
    }

    private static QuickStyleViewModel? StyleFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as QuickStyleViewModel;

    private void OnQuickStyleClick(object sender, RoutedEventArgs e) => ApplyQuickStyle(StyleFrom(sender));

    private void OnQuickStyleApply(object sender, RoutedEventArgs e) => ApplyQuickStyle(StyleFrom(sender));

    private void ApplyQuickStyle(QuickStyleViewModel? vm)
    {
        if (vm is null)
        {
            return;
        }

        ApplyColor(vm.Style.Color);
        _suppressUiEvents = true;
        foreach (RadioButton rb in SwatchPanel.Children.OfType<RadioButton>())
        {
            rb.IsChecked = string.Equals((string)rb.Tag, HexBox.Text, StringComparison.OrdinalIgnoreCase);
        }

        _suppressUiEvents = false;
        ThicknessSlider.Value = Math.Clamp(vm.Style.StrokeWidth, ThicknessSlider.Minimum, ThicknessSlider.Maximum);
        Editor.SetStrokeWidth(ThicknessSlider.Value);
        SetStatus($"Quick Style \"{vm.Name}\" applied.");
    }

    private void OnQuickStyleToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (StyleFrom(sender) is not { } vm)
        {
            return;
        }

        _services.UpdateSettings(s => s with
        {
            FavoriteQuickStyles = vm.Favorite ? [.. s.FavoriteQuickStyles.Where(id => id != vm.Style.Id)] : [.. s.FavoriteQuickStyles, vm.Style.Id],
        });
        RefreshQuickStyles();
    }

    private void OnQuickStyleDelete(object sender, RoutedEventArgs e)
    {
        if (StyleFrom(sender) is { CanDelete: true } vm)
        {
            _services.UpdateSettings(s => s with
            {
                UserQuickStyles = [.. s.UserQuickStyles.Where(q => q.Id != vm.Style.Id)],
                FavoriteQuickStyles = [.. s.FavoriteQuickStyles.Where(id => id != vm.Style.Id)],
            });
            RefreshQuickStyles();
        }
    }

    private void OnSaveQuickStyle(object sender, RoutedEventArgs e)
    {
        string? name = InputDialog.Ask(this, "Save Quick Style", "Name for this color and thickness:", $"{HexBox.Text} {ThicknessSlider.Value:0}px");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var style = new QuickStyle { Name = name.Trim(), Color = Editor.CurrentColor, StrokeWidth = Editor.StrokeWidth };
        _services.UpdateSettings(s => s with { UserQuickStyles = [style, .. s.UserQuickStyles] });
        RefreshQuickStyles();
        SetStatus($"Saved Quick Style \"{style.Name}\".");
    }

    // ------------------------------------------------------------------ output

    private void OnCopy(object sender, RoutedEventArgs e) => _ = CopyAsync();

    /// <summary>Offline OCR of the open capture as it would be exported (redactions burned in).</summary>
    private void OnExtractText(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            SetStatus("Open a capture first, then choose Image → Extract text.");
            return;
        }

        Ocr.TextRecognitionWindow.Show(this, RenderFinalImage(), _current.Title);
    }

    private void OnSave(object sender, RoutedEventArgs e) => _ = ExportAsync();

    internal PixelBuffer RenderFinalImage()
    {
        if (_current is null)
        {
            throw new InvalidOperationException("Nothing to export.");
        }

        Editor.CommitTextEdit();
        return ImageExporter.Flatten(_current.Document.Image, _current.Document.Annotations);
    }

    private async Task CopyAsync()
    {
        if (_current is null)
        {
            return;
        }

        if (await CopyImageAsync(RenderFinalImage(), "Copied to clipboard.").ConfigureAwait(true))
        {
            _exportedSinceLoad = true;
        }
    }

    private async Task<bool> CopyImageAsync(PixelBuffer image, string doneMessage)
    {
        try
        {
            await ImageExporter.CopyToClipboardAsync(image).ConfigureAwait(true);
            SetStatus(doneMessage);
            return true;
        }
#pragma warning disable CA1031 // Report clipboard failures instead of crashing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MessageBox.Show(this, $"The image could not be copied to the clipboard.\n\n{ex.Message}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private async Task ExportAsync()
    {
        if (_current is null)
        {
            return;
        }

        Editor.CommitTextEdit();
        if (await ExportImageAsync(RenderFinalImage(), _current.Title, _current.CapturedAt).ConfigureAwait(true))
        {
            _exportedSinceLoad = true;
            if (!_current.InLibrary)
            {
                _current.Document.MarkSaved();
                UpdateSaveState();
            }
        }
    }

    /// <summary>Asks for a file and writes a flattened PNG/JPEG (exports are separate from the Library).</summary>
    internal async Task<bool> ExportImageAsync(PixelBuffer flattened, string title, DateTime capturedAt)
    {
        var dialog = new SaveFileDialog
        {
            Title = $"Export \"{title}\"",
            Filter = ExportRules.SaveDialogFilter,
            FilterIndex = 1,
            FileName = ExportRules.DefaultFileName(capturedAt, ImageFileFormat.Png),
            InitialDirectory = _lastExportDirectory ?? DefaultExportDirectory(),
            AddExtension = true,
            DefaultExt = ".png",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        (string path, ImageFileFormat format) = ExportRules.ResolveSaveTarget(dialog.FileName, dialog.FilterIndex);
        try
        {
            await ImageExporter.SaveAsync(flattened, path, format).ConfigureAwait(true);
            _lastExportDirectory = Path.GetDirectoryName(path);
            SetStatus($"Exported {path}");
            return true;
        }
#pragma warning disable CA1031 // Report I/O failures (permissions, disk full) instead of crashing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MessageBox.Show(this, $"The image could not be exported.\n\n{ex.Message}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static string DefaultExportDirectory()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string screenshots = Path.Combine(pictures, "Screenshots");
        return Directory.Exists(screenshots) ? screenshots : pictures;
    }

    private void OnSaveToLibrary(object sender, RoutedEventArgs e)
    {
        if (_current is { InLibrary: false, Creation: null } s)
        {
            s.Creation = CreateInLibraryAsync(s);
            UpdateSaveState();
            SetStatus("Saving to the Library…");
        }
    }

    private async void OnExportPackage(object sender, RoutedEventArgs e)
    {
        if (_current?.CaptureId is not { } id)
        {
            return;
        }

        await SaveSessionAsync(_current).ConfigureAwait(true);
        var dialog = new SaveFileDialog
        {
            Title = "Export editable capture",
            Filter = $"SnapForge capture (*{CaptureFormat.PackageExtension})|*{CaptureFormat.PackageExtension}",
            FileName = _current.Title + CaptureFormat.PackageExtension,
            AddExtension = true,
            DefaultExt = CaptureFormat.PackageExtension,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (_current.Document.HasReversibleRedactions &&
            MessageBox.Show(this, "This editable capture still contains the original pixels under its redactions. Anyone you give the .sfcap file to can undo the redactions.\n\nExport anyway? (Use File → Sanitize first, or export a PNG/JPEG instead.)",
                ProductInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _services.Writer.RunAsync(() => _services.Library.ExportPackage(id, dialog.FileName)).ConfigureAwait(true);
            SetStatus($"Exported {dialog.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The capture could not be exported.\n\n{ex.Message}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnImportPackage(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import editable capture", Filter = $"SnapForge capture (*{CaptureFormat.PackageExtension})|*{CaptureFormat.PackageExtension}" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            CaptureRecord record = await _services.Writer.RunAsync(() => _services.Library.ImportPackage(dialog.FileName)).ConfigureAwait(true);
            await OpenCaptureAsync(record.Id).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The file could not be imported.\n\n{ex.Message}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ------------------------------------------------------------------ sanitize

    private async void OnSanitize(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        Editor.CommitTextEdit();
        if (MessageBox.Show(this,
            "Sanitize permanently removes hidden content from this capture:\n\n" +
            "• Redactions are burned into the stored image — the original pixels are deleted from the Library copy.\n" +
            "• Annotations that lie under a redaction are flattened into the image and removed as editable objects.\n" +
            "• The thumbnail is regenerated and the undo history is cleared.\n\n" +
            "This cannot be undone. Copies you exported or backed up earlier are not changed.\n\nSanitize now?",
            $"Sanitize \"{_current.Title}\"", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await SanitizeSessionAsync(_current).ConfigureAwait(true);
    }

    internal async Task SanitizeSessionAsync(EditorSession session)
    {
        if (session.Creation is { IsCompleted: false } creation)
        {
            await creation.ConfigureAwait(true);
        }

        EditorDocument doc = session.Document;
        int flattenedCount = doc.Sanitize((image, annotations) => ImageExporter.Flatten(image, annotations));
        session.BaseVersion++;
        if (session.CaptureId is { } id)
        {
            PixelBuffer baseCopy = doc.BaseImage.Clone();
            Annotation[] annotations = [.. doc.Annotations];
            PixelBuffer flattened = ImageExporter.Flatten(doc.Image, doc.Annotations);
            try
            {
                await _services.Writer.RunAsync(() => _services.Library.Sanitize(id, baseCopy, annotations, flattened)).ConfigureAwait(true);
                session.SavedBaseVersion = session.BaseVersion;
                doc.MarkSaved();
                _services.Thumbnails.Invalidate(id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                session.LastSaveError = $"Sanitizing the Library copy failed: {ex.Message}";
            }
        }

        UpdateCommandState();
        RefreshTray();
        SetStatus(flattenedCount > 0 ? $"Sanitized. {flattenedCount} hidden annotation(s) were flattened." : "Sanitized. The original pixels under redactions were removed.");
    }

    /// <summary>Sanitizes library captures that are not open (from the Library view).</summary>
    internal async Task SanitizeRecordsAsync(IReadOnlyCollection<Guid> ids)
    {
        foreach (Guid id in ids)
        {
            if (_sessions.TryGetValue(id, out EditorSession? open))
            {
                await SanitizeSessionAsync(open).ConfigureAwait(true);
                continue;
            }

            CaptureContent content = await _services.Writer.RunAsync(() => _services.Library.Load(id)).ConfigureAwait(true);
            var doc = new EditorDocument(content.BaseImage, content.Annotations);
            doc.Sanitize((image, annotations) => ImageExporter.Flatten(image, annotations));
            PixelBuffer flattened = ImageExporter.Flatten(doc.Image, doc.Annotations);
            Annotation[] annotations = [.. doc.Annotations];
            await _services.Writer.RunAsync(() => _services.Library.Sanitize(id, doc.BaseImage, annotations, flattened)).ConfigureAwait(true);
            doc.Discard();
            _services.Thumbnails.Invalidate(id);
        }

        SetStatus(ids.Count == 1 ? "Sanitized 1 capture." : $"Sanitized {ids.Count} captures.");
    }

    // ------------------------------------------------------------------ close / exit

    private async void OnCloseCapture(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
        {
            await CloseSessionAsync(_current, removeFromTray: false).ConfigureAwait(true);
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => _ = App.ExitAsync();

    private void OnSettings(object sender, RoutedEventArgs e) => App.ShowSettings(this);

    internal bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (AllowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_services.Settings.CloseToNotificationArea && !_exiting && App.HasNotificationIcon)
        {
            _ = FlushAllAsync();
            Hide();
            App.NotifyOnce("SnapForge is still running", "Captures and hotkeys keep working. Right-click the SnapForge icon in the notification area to exit.");
            return;
        }

        _ = CloseForRealAsync();
    }

    /// <summary>Saves everything, asks about in-memory captures, then closes. Returns false if the user cancelled.</summary>
    internal async Task<bool> CloseForRealAsync()
    {
        _exiting = true;
        Editor.CommitTextEdit();
        int unsaved = _sessions.Values.Count(s => !s.InLibrary && s.Creation is null && s.Document.IsDirty);
        if (unsaved > 0 && MessageBox.Show(this,
                $"{unsaved} capture(s) taken without history have not been exported and will be lost.\n\nClose anyway?",
                ProductInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            _exiting = false;
            return false;
        }

        await FlushAllAsync().ConfigureAwait(true);
        foreach (EditorSession s in _sessions.Values)
        {
            s.Document.Discard();
        }

        _sessions.Clear();
        AllowClose = true;
        Close();
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _autosaveTimer.Stop();
        foreach (EditorSession s in _sessions.Values)
        {
            s.Document.Discard();
        }
    }

    // ------------------------------------------------------------------ keyboard

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (CaptureController.IsBusy)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        bool alt = mods.HasFlag(ModifierKeys.Alt);
        bool typing = Editor.IsEditingText || Keyboard.FocusedElement is TextBox;
        bool editorVisible = EditorWorkspace.Visibility == Visibility.Visible;

        if (key == Key.F1)
        {
            Handle(e, () => OnShortcuts(this, e));
            return;
        }

        if (ctrl && !alt)
        {
            switch (key)
            {
                case Key.D1 or Key.NumPad1: Handle(e, () => OnCaptureRegion(this, e)); return;
                case Key.D2 or Key.NumPad2: Handle(e, () => OnCaptureWindow(this, e)); return;
                case Key.D3 or Key.NumPad3: Handle(e, () => OnCaptureDisplay(this, e)); return;
                case Key.D4 or Key.NumPad4: Handle(e, () => OnCaptureAll(this, e)); return;
                case Key.D5 or Key.NumPad5: Handle(e, () => OnCaptureActiveWindow(this, e)); return;
                case Key.R when !shift: Handle(e, () => OnRepeatLastCapture(this, e)); return;
                case Key.V when shift: Handle(e, () => OnNewFromClipboard(this, e)); return;
                case Key.N: Handle(e, () => OnNewCapture(this, e)); return;
                case Key.L: Handle(e, ShowLibraryWorkspace); return;
                case Key.E: Handle(e, ShowEditorWorkspace); return;
                case Key.OemComma: Handle(e, () => OnSettings(this, e)); return;
                case Key.S when _current is not null: Handle(e, () => _ = ExportAsync()); return;
                case Key.W when _current is not null && editorVisible: Handle(e, () => OnCloseCapture(this, e)); return;
                case Key.T when shift && _current is not null: Handle(e, () => OnExtractText(this, e)); return;
                case Key.P when shift && _current?.CaptureId is { } pinId:
                    Handle(e, () => _services.Tray.SetPinned(pinId, !_services.Tray.Items.Any(i => i.Id == pinId && i.Pinned)));
                    return;
            }

            if (!typing && _current is not null && editorVisible)
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

        if (ctrl && alt && (key is Key.D0 or Key.NumPad0) && _current is not null)
        {
            Handle(e, ApplyActualPixelsZoom);
            return;
        }

        if (typing || ctrl || alt || _current is null || !editorVisible)
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
            case Key.H: Handle(e, () => HighlightTool.IsChecked = true); return;
            case Key.T: Handle(e, () => TextTool.IsChecked = true); return;
            case Key.N: Handle(e, () => StepTool.IsChecked = true); return;
            case Key.X: Handle(e, () => RedactTool.IsChecked = true); return;
            case Key.C: Handle(e, () => CropTool.IsChecked = true); return;
        }
    }

    private static void Handle(KeyEventArgs e, Action action)
    {
        e.Handled = true;
        action();
    }

    // ------------------------------------------------------------------ view settings

    private void ApplySettingsToUi()
    {
        AppSettings s = _services.Settings;
        TrayPanel.Visibility = s.ShowRecentTray ? Visibility.Visible : Visibility.Collapsed;
        bool panel = s.ShowPropertiesPanel;
        PropertiesPanel.Visibility = panel ? Visibility.Visible : Visibility.Collapsed;
        PanelSplitter.Visibility = panel ? Visibility.Visible : Visibility.Collapsed;
        PanelColumn.Width = panel ? new GridLength(Math.Clamp(s.PropertiesPanelWidth, 180, 520)) : new GridLength(0);
        PanelToggle.IsChecked = panel;
        HotkeySummary.Text = s.CaptureHotkey is { } hk ? $"Global capture hotkey: {hk}  ·  change it in Settings" : "No global capture hotkey set (Settings → Hotkeys)";
    }

    private void OnToggleTray(object sender, RoutedEventArgs e) =>
        _services.UpdateSettings(s => s with { ShowRecentTray = !s.ShowRecentTray });

    private void OnTogglePanel(object sender, RoutedEventArgs e) =>
        _services.UpdateSettings(s => s with { ShowPropertiesPanel = !s.ShowPropertiesPanel });

    private void OnPanelSplitterDragCompleted(object sender, DragCompletedEventArgs e) =>
        _services.UpdateSettings(s => s with { PropertiesPanelWidth = PanelColumn.ActualWidth });

    private void OnThemeMenu(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse(tag, out ThemePreference theme))
        {
            _services.UpdateSettings(s => s with { Theme = theme });
            ThemeManager.Apply(theme);
        }
    }

    // ------------------------------------------------------------------ zoom

    private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomBy(1.25);

    private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);

    private void OnZoomFit(object sender, RoutedEventArgs e) => ApplyFitZoom();

    private void OnZoomActual(object sender, RoutedEventArgs e) => ApplyActualPixelsZoom();

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _current is not null)
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

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_current is null)
        {
            return;
        }

        if (_fitMode)
        {
            ApplyFitZoom();
        }
        else
        {
            SetZoom(ZoomTransform.ScaleX, fitMode: false);
        }
    }

    private void ApplyFitZoom()
    {
        if (_current is null)
        {
            return;
        }

        double availableW = Math.Max(1, Viewport.ActualWidth - 50);
        double availableH = Math.Max(1, Viewport.ActualHeight - 50);
        double fit = Math.Min(availableW / _current.Document.Image.Width, availableH / _current.Document.Image.Height);
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
        ZoomText.Text = $"{zoom / ActualPixelsZoom() * 100:0}%";
    }

    // ------------------------------------------------------------------ help

    private void OnShortcuts(object sender, RoutedEventArgs e) => ShowShortcuts(this);

    internal void ShowShortcuts(Window owner) => MessageBox.Show(owner,
        "Capture: Ctrl+N (current settings) · Ctrl+1 Region · Ctrl+2 Window · Ctrl+3 Display · Ctrl+4 Full screen\n" +
        $"Global capture hotkey: {_services.Settings.CaptureHotkey?.ToString() ?? "none"} (Settings → Hotkeys)\n\n" +
        "Workspaces: Ctrl+E Editor · Ctrl+L Library · Ctrl+, Settings\n\n" +
        "Tools: V Select · A Arrow · L Line · R Rectangle · E Ellipse · H Highlight · T Text · N Step · X Redact · C Crop\n" +
        "Edit: Ctrl+Z Undo · Ctrl+Y Redo · Del Delete · Esc Cancel\n" +
        "Output: Ctrl+C Copy · Ctrl+S Export\n" +
        "View: Ctrl+= / Ctrl+− zoom · Ctrl+0 fit · Ctrl+Alt+0 actual pixels · Ctrl+wheel zoom\n" +
        "Tray: Ctrl+Shift+P pin/unpin the current capture · Ctrl+W close capture\n\n" +
        "Library: Enter open · F2 rename · Del move to Trash · Ctrl+A select all · arrows move",
        "Keyboard shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnPrivacyInfo(object sender, RoutedEventArgs e) => App.ShowPrivacyNotice(this);

    private void OnAbout(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        $"{ProductInfo.Name} — Capture. Create. Share.\n\n{SnapForge.App.Services.InstanceInfo.Describe()}\n\n" +
        $"Program: {Environment.ProcessPath}\nLibrary: {_services.Library.Root}\n\n" +
        "Free and open source under the MIT License — Copyright (c) 2026 bazeocrisy. Third-party notices: THIRD-PARTY-NOTICES.md.\n" +
        "No network access, no telemetry, no account.",
        $"About {ProductInfo.Name}", MessageBoxButton.OK, MessageBoxImage.Information);

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

    /// <summary>Applies saved tool/color/thickness after start-up.</summary>
    internal void RestoreToolSettings()
    {
        AppSettings s = _services.Settings;
        _suppressUiEvents = true;
        ThicknessSlider.Value = Math.Clamp(s.LastStrokeWidth, 1, 20);
        _suppressUiEvents = false;
        Editor.SetStrokeWidth(ThicknessSlider.Value);
        Editor.SetColor(s.LastColor);
        HexBox.Text = $"#{s.LastColor.R:X2}{s.LastColor.G:X2}{s.LastColor.B:X2}";
        _suppressUiEvents = true;
        foreach (RadioButton rb in SwatchPanel.Children.OfType<RadioButton>())
        {
            rb.IsChecked = string.Equals((string)rb.Tag, HexBox.Text, StringComparison.OrdinalIgnoreCase);
        }

        _suppressUiEvents = false;
        UpdateToolProperties();
    }

    internal static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    internal static string FormatDate(DateTime utc) => utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
