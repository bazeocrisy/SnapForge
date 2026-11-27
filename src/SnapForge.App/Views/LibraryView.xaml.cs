using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapForge.App.Dialogs;
using SnapForge.App.Rendering;
using SnapForge.App.Services;
using SnapForge.Core;
using SnapForge.Core.Editing;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.App.Views;

internal sealed class LibraryHeaderRow(string title)
{
    public string Title { get; } = title;
}

internal sealed class LibraryTileRow(IReadOnlyList<LibraryTileViewModel> tiles)
{
    public IReadOnlyList<LibraryTileViewModel> Tiles { get; } = tiles;
}

internal sealed class LibraryRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Header { get; set; }

    public DataTemplate? Tiles { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is LibraryHeaderRow ? Header : Tiles;
}

/// <summary>One capture tile. The thumbnail loads lazily when the tile is first shown (virtualized rows).</summary>
internal sealed class LibraryTileViewModel : ObservableObject
{
    private readonly Func<LibraryTileViewModel, Task> _loadThumbnail;
    private ImageSource? _thumbnail;
    private bool _thumbnailRequested;
    private bool _isSelected;

    public LibraryTileViewModel(CaptureRecord record, int index, double size, Func<LibraryTileViewModel, Task> loadThumbnail)
    {
        Record = record;
        Index = index;
        TileWidth = size;
        ImageHeight = Math.Round(size * 0.62);
        _loadThumbnail = loadThumbnail;
    }

    public CaptureRecord Record { get; }

    public int Index { get; }

    public double TileWidth { get; }

    public double ImageHeight { get; }

    public string Title => Record.Title;

    public bool Favorite => Record.IsFavorite;

    public string Subtitle => Record.Damage is not null
        ? "Damaged — cannot be opened"
        : Record.IsVideo
            ? $"{Record.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)} · {Record.Width}×{Record.Height} · {Video.VideoPlayerWindow.FormatSize(Record.SizeBytes)}"
            : $"{Record.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)} · {Record.Width}×{Record.Height}";

    public bool IsVideo => Record.IsVideo;

    public string DurationText => Video.VideoPlayerWindow.Format(TimeSpan.FromMilliseconds(Record.DurationMs));

    public bool HasWarning => Record.Damage is not null || Record.HasReversibleRedactions || Record.Recovered;

    public string WarningText => Record.Damage is not null ? "Damaged" : Record.Recovered ? "Recovered" : "Unsanitized redactions";

    public string Tooltip => string.Join("\n", new[]
    {
        Record.Title,
        Subtitle,
        Record.Meta.Tags.Count > 0 ? "Tags: " + string.Join(", ", Record.Meta.Tags) : null,
        Record.Meta.Folder.Length > 0 ? "Folder: " + Record.Meta.Folder : null,
        Record.HasReversibleRedactions ? "The Library copy still holds original pixels under redactions (Sanitize removes them)." : null,
        Record.IsVideo ? $"Video · {DurationText} · {Record.FramesPerSecond} FPS · {(Record.HasAudio ? "with sound" : "no sound")}. Videos cannot be redacted or sanitized." : null,
        Record.Recovered ? "Recovered after the recording was interrupted; the last seconds before the interruption may be missing." : null,
        Record.VideoFragmented ? "Kept in its recording (fragmented) form because it could not be optimised — usually too little free disk space. Windows players may show sound slightly out of step." : null,
        Record.IsDeleted ? "In Trash" : null,
    }.Where(s => s is not null));

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested)
            {
                _thumbnailRequested = true;
                _ = _loadThumbnail(this);
            }

            return _thumbnail;
        }

        set => Set(ref _thumbnail, value);
    }
}

/// <summary>
/// The Library workspace: sidebar views (All, Recent, Favorites, Trash, tags, folders), search, sort, adjustable
/// thumbnails, date-grouped virtualized grid with multi-selection, and capture management actions.
/// </summary>
public partial class LibraryView : UserControl
{
    private MainWindow? _host;
    private AppServices? _services;
    private LibraryQuery _query = new();
    private List<LibraryTileViewModel> _tiles = [];
    private readonly HashSet<Guid> _selected = [];
    private int _anchor = -1;
    private int _focusIndex = -1;
    private int _pendingSingleSelect = -1;
    private Point? _dragStart;
    private LibraryTileViewModel? _dragTile;
    private int _perRow = 4;
    private readonly DispatcherTimer _searchDelay;
    private bool _initializing = true;

    public LibraryView()
    {
        InitializeComponent();
        _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            _query = _query with { Search = SearchBox.Text };
            Refresh();
        };
        _initializing = false;
    }

    internal IReadOnlyList<Guid> SelectedIds => [.. _tiles.Where(t => _selected.Contains(t.Record.Id)).Select(t => t.Record.Id)];

    internal int VisibleCount => _tiles.Count;

    internal void Initialize(MainWindow host, AppServices services)
    {
        _host = host;
        _services = services;
        _initializing = true;
        SizeSlider.Value = Math.Clamp(services.Settings.LibraryThumbnailSize, (int)SizeSlider.Minimum, (int)SizeSlider.Maximum);
        _query = _query with { Sort = services.Settings.LibrarySort };
        _initializing = false;
        UpdateSortLabel();
    }

    internal void OnLibraryChanged(IReadOnlyCollection<Guid> ids)
    {
        if (IsVisible)
        {
            Refresh();
        }
    }

    internal void FocusGrid() => Dispatcher.BeginInvoke(() => Rows.Focus(), DispatcherPriority.Input);

    internal void SetSearch(string text)
    {
        SearchBox.Text = text;
        _query = _query with { Search = text };
        Refresh();
    }

    internal void SelectView(Core.Library.LibraryView view, string? key = null)
    {
        _query = _query with { View = view, Key = key };
        Refresh();
    }

    /// <summary>Shows and selects a capture (e.g. from the tray's "Show in Library").</summary>
    internal void Reveal(Guid id)
    {
        AllView.IsChecked = true;
        _query = _query with { View = Core.Library.LibraryView.All, Key = null, Search = string.Empty };
        SearchBox.Text = string.Empty;
        Refresh();
        _selected.Clear();
        _selected.Add(id);
        ApplySelection();
    }

    /// <summary>Re-runs the query and rebuilds the rows (virtualized; thumbnails load lazily).</summary>
    internal void Refresh()
    {
        if (_services is null)
        {
            return;
        }

        CaptureLibrary library = _services.Library;
        DateTime now = DateTime.UtcNow;
        IReadOnlyList<CaptureRecord> records = library.Query(_query, now);
        double size = SizeSlider.Value;
        _tiles = [.. records.Select((r, i) => new LibraryTileViewModel(r, i, size, LoadThumbnailAsync))];
        _selected.IntersectWith(_tiles.Select(t => t.Record.Id));
        BuildRows();
        UpdateSidebar(now);
        ApplySelection();

        bool empty = _tiles.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        (EmptyTitle.Text, EmptyText.Text) = (_query.Search.Length > 0, _query.View) switch
        {
            (true, _) => ("No matches", $"Nothing matches \"{_query.Search}\" in this view."),
            (_, Core.Library.LibraryView.Trash) => ("Trash is empty", "Deleted captures stay here until you empty the Trash."),
            (_, Core.Library.LibraryView.Favorites) => ("No favorites", "Right-click a capture → Toggle favorite."),
            (_, Core.Library.LibraryView.Videos) => ("No recordings yet", "Choose Video in the capture panel, or press the recording hotkey, to record your screen."),
            _ => ("No captures yet", "Captures you take are saved here automatically (unless you turn that off in the Capture window)."),
        };
        bool trash = _query.View == Core.Library.LibraryView.Trash;
        DeleteButton.Content = trash ? "Delete permanently…" : "Move to Trash";
        RestoreButton.Visibility = trash ? Visibility.Visible : Visibility.Collapsed;
        EmptyTrashButton.Visibility = trash ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildRows()
    {
        double available = Math.Max(200, Rows.ActualWidth - 40);
        _perRow = Math.Max(1, (int)(available / (SizeSlider.Value + 12)));
        var rows = new List<object>();
        bool group = _query.Sort is LibrarySort.NewestFirst or LibrarySort.OldestFirst or LibrarySort.RecentlyModified;
        if (group)
        {
            Func<CaptureRecord, DateTime> key = _query.Sort == LibrarySort.RecentlyModified || _query.View == Core.Library.LibraryView.Recent
                ? r => r.RecentActivityUtc
                : r => r.CreatedUtc;
            var byRecord = _tiles.ToDictionary(t => t.Record.Id);
            foreach ((string title, IReadOnlyList<CaptureRecord> items) in DateGrouping.Group(_tiles.Select(t => t.Record), DateTime.Now, key))
            {
                rows.Add(new LibraryHeaderRow($"{title}  ·  {items.Count}"));
                AddTileRows(rows, [.. items.Select(r => byRecord[r.Id])]);
            }
        }
        else
        {
            AddTileRows(rows, _tiles);
        }

        Rows.ItemsSource = rows;
    }

    private void AddTileRows(List<object> rows, List<LibraryTileViewModel> tiles)
    {
        for (int i = 0; i < tiles.Count; i += _perRow)
        {
            rows.Add(new LibraryTileRow([.. tiles.Skip(i).Take(_perRow)]));
        }
    }

    private async Task LoadThumbnailAsync(LibraryTileViewModel tile)
    {
        if (_services is null || tile.Record.Damage is not null)
        {
            return;
        }

        tile.Thumbnail = await _services.Thumbnails.GetAsync(_services.Library, tile.Record.Id).ConfigureAwait(true);
    }

    private void UpdateSidebar(DateTime now)
    {
        CaptureLibrary library = _services!.Library;
        IReadOnlyList<CaptureRecord> all = library.Records;
        AllCount.Text = all.Count(r => !r.IsDeleted).ToString(CultureInfo.CurrentCulture);
        RecentCount.Text = new LibraryQuery { View = Core.Library.LibraryView.Recent }.Apply(all, now).Count().ToString(CultureInfo.CurrentCulture);
        VideosCount.Text = new LibraryQuery { View = Core.Library.LibraryView.Videos }.Apply(all, now).Count().ToString(CultureInfo.CurrentCulture);
        FavoritesCount.Text = all.Count(r => !r.IsDeleted && r.IsFavorite).ToString(CultureInfo.CurrentCulture);
        TrashCount.Text = all.Count(r => r.IsDeleted).ToString(CultureInfo.CurrentCulture);

        RebuildKeyList(TagsPanel, NoTagsText, library.AllTags(), Core.Library.LibraryView.Tag, "IconTag");
        RebuildKeyList(FoldersPanel, NoFoldersText, library.AllFolders(), Core.Library.LibraryView.Folder, "IconFolder");

        StorageUsage usage = library.GetUsage();
        StorageText.Text = $"{usage.CaptureCount} captures · {MainWindow.FormatBytes(usage.TotalBytes)}" +
                           (usage.TrashCount > 0 ? $"\nTrash: {usage.TrashCount} · {MainWindow.FormatBytes(usage.TrashBytes)}" : string.Empty) +
                           (library.IsIndexed ? string.Empty : "\nLoading the library…");
        StoragePathText.Text = usage.Root;
        StoragePathText.ToolTip = usage.Root;
    }

    private void RebuildKeyList(Panel panel, TextBlock emptyText, IReadOnlyList<string> keys, Core.Library.LibraryView view, string icon)
    {
        panel.Children.Clear();
        foreach (string key in keys)
        {
            var rb = new RadioButton
            {
                GroupName = "View",
                Style = (Style)FindResource("SidebarItem"),
                Tag = (view, key),
                IsChecked = _query.View == view && string.Equals(_query.Key, key, StringComparison.OrdinalIgnoreCase),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new System.Windows.Shapes.Path { Style = (Style)FindResource("IconPath"), Data = (Geometry)FindResource(icon), Width = 22 },
                        new TextBlock { Text = key, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                },
            };
            rb.Checked += OnViewChecked;
            panel.Children.Add(rb);
        }

        emptyText.Visibility = keys.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnViewChecked(object sender, RoutedEventArgs e)
    {
        if (_initializing || _services is null)
        {
            return;
        }

        _query = sender switch
        {
            FrameworkElement { Tag: ValueTuple<Core.Library.LibraryView, string> t } => _query with { View = t.Item1, Key = t.Item2 },
            FrameworkElement { Tag: string s } when Enum.TryParse(s, out Core.Library.LibraryView v) => _query with { View = v, Key = null },
            _ => _query,
        };
        _selected.Clear();
        Refresh();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private void OnSortClick(object sender, RoutedEventArgs e) => OpenMenu(SortButton);

    private void OnMore(object sender, RoutedEventArgs e) => OpenMenu(MoreButton);

    private static void OpenMenu(Button button)
    {
        if (button.ContextMenu is { } menu)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnSortChosen(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse(tag, out LibrarySort sort))
        {
            _query = _query with { Sort = sort };
            _services?.UpdateSettings(s => s with { LibrarySort = sort });
            UpdateSortLabel();
            Refresh();
        }
    }

    private void UpdateSortLabel() => SortButton.Content = "Sort: " + _query.Sort switch
    {
        LibrarySort.OldestFirst => "Oldest ▾",
        LibrarySort.TitleAscending => "Name ▾",
        LibrarySort.LargestFirst => "Largest ▾",
        LibrarySort.RecentlyModified => "Recently opened ▾",
        _ => "Newest ▾",
    };

    private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _services is null)
        {
            return;
        }

        _services.UpdateSettings(s => s with { LibraryThumbnailSize = (int)e.NewValue });
        Refresh();
    }

    private void OnRowsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && _services is not null)
        {
            int perRow = Math.Max(1, (int)(Math.Max(200, Rows.ActualWidth - 40) / (SizeSlider.Value + 12)));
            if (perRow != _perRow)
            {
                BuildRows();
            }
        }
    }

    // ------------------------------------------------------------------ selection

    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryTileViewModel tile })
        {
            return;
        }

        Rows.Focus();
        if (e.ClickCount == 2)
        {
            _ = _host?.OpenCaptureAsync(tile.Record.Id);
            e.Handled = true;
            return;
        }

        // Pressing on a tile that is already part of a multi-selection keeps the selection (so it can be dragged);
        // a plain click without dragging narrows it to that tile on release.
        ModifierKeys mods = Keyboard.Modifiers;
        _pendingSingleSelect = mods == ModifierKeys.None && _selected.Contains(tile.Record.Id) && _selected.Count > 1 ? tile.Index : -1;
        if (_pendingSingleSelect < 0)
        {
            ClickSelect(tile.Index, mods);
        }

        _dragStart = e.GetPosition(this);
        _dragTile = tile;
        e.Handled = true;
    }

    private void OnTileMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start || _dragTile is null || _host is null)
        {
            return;
        }

        Vector d = e.GetPosition(this) - start;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        _dragTile = null;
        _pendingSingleSelect = -1;
        List<Guid> ids = [.. SelectedRecords().Where(r => !r.IsDeleted && r.Damage is null).Select(r => r.Id)];
        if (ids.Count > 0)
        {
            _host.StartCaptureDrag((DependencyObject)sender, ids);
        }

        e.Handled = true;
    }

    private void OnTileMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingSingleSelect >= 0)
        {
            ClickSelect(_pendingSingleSelect, ModifierKeys.None);
        }

        _pendingSingleSelect = -1;
        _dragStart = null;
        _dragTile = null;
    }

    private void OnEditVideo(object sender, RoutedEventArgs e)
    {
        if (SelectedRecords() is [{ IsVideo: true } record] && AppServices.Current is { } services)
        {
            new Video.VideoEditorWindow(services, record, services.Library.VideoPath(record.Id)).Show();
        }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (_host is not null)
        {
            await _host.ImportWithDialogAsync().ConfigureAwait(true);
        }
    }

    /// <summary>The on-screen tile of a capture, if realized (tests).</summary>
    internal FrameworkElement? TileElement(Guid id) => FindTile(Rows, id);

    private static FrameworkElement? FindTile(DependencyObject parent, Guid id)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Tag: LibraryTileViewModel t } fe && t.Record.Id == id)
            {
                return fe;
            }

            if (FindTile(child, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Selects captures by id (tests and drag set-up).</summary>
    internal void SelectIds(IEnumerable<Guid> ids)
    {
        _selected.Clear();
        foreach (Guid id in ids)
        {
            _selected.Add(id);
        }

        ApplySelection();
    }

    private void ClickSelect(int index, ModifierKeys mods)
    {
        if (index < 0 || index >= _tiles.Count)
        {
            return;
        }

        Guid id = _tiles[index].Record.Id;
        if (mods.HasFlag(ModifierKeys.Shift) && _anchor >= 0)
        {
            if (!mods.HasFlag(ModifierKeys.Control))
            {
                _selected.Clear();
            }

            for (int i = Math.Min(_anchor, index); i <= Math.Max(_anchor, index); i++)
            {
                _selected.Add(_tiles[i].Record.Id);
            }
        }
        else if (mods.HasFlag(ModifierKeys.Control))
        {
            if (!_selected.Remove(id))
            {
                _selected.Add(id);
            }

            _anchor = index;
        }
        else
        {
            _selected.Clear();
            _selected.Add(id);
            _anchor = index;
        }

        _focusIndex = index;
        ApplySelection();
    }

    private void OnGridBackgroundClick(object sender, MouseButtonEventArgs e)
    {
        Rows.Focus();
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _selected.Clear();
            ApplySelection();
        }
    }

    private void OnTileContextOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { Tag: LibraryTileViewModel tile } } menu)
        {
            if (!_selected.Contains(tile.Record.Id))
            {
                _selected.Clear();
                _selected.Add(tile.Record.Id);
                _anchor = _focusIndex = tile.Index;
                ApplySelection();
            }

            // Videos: Play instead of Open in Editor; image-only commands are not offered.
            bool video = tile.IsVideo;
            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
            {
                switch (item.Tag as string)
                {
                    case "open":
                        item.Header = video ? "_Play" : "_Open in Editor";
                        break;
                    case "export":
                        item.Header = video ? "_Export MP4 video…" : "_Export image (PNG, JPEG, BMP, TIFF)…";
                        break;
                    case "image-only":
                        item.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
                        break;
                    case "video-only":
                        item.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
                        break;
                }
            }
        }
    }

    private void ApplySelection()
    {
        foreach (LibraryTileViewModel t in _tiles)
        {
            t.IsSelected = _selected.Contains(t.Record.Id);
        }

        int n = _selected.Count;
        SelectionText.Text = n == 0 ? $"{_tiles.Count} capture(s)" : $"{n} of {_tiles.Count} selected";
        OpenButton.IsEnabled = n == 1;
        ExportButton.IsEnabled = DeleteButton.IsEnabled = MoreButton.IsEnabled = RestoreButton.IsEnabled = n > 0;
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (_tiles.Count == 0 || Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        ModifierKeys mods = Keyboard.Modifiers;
        int current = _focusIndex < 0 ? 0 : _focusIndex;
        int target = e.Key switch
        {
            Key.Right => current + 1,
            Key.Left => current - 1,
            Key.Down => current + _perRow,
            Key.Up => current - _perRow,
            Key.Home => 0,
            Key.End => _tiles.Count - 1,
            _ => int.MinValue,
        };
        if (target != int.MinValue)
        {
            target = Math.Clamp(target, 0, _tiles.Count - 1);
            if (mods.HasFlag(ModifierKeys.Shift))
            {
                if (_anchor < 0)
                {
                    _anchor = current;
                }

                _selected.Clear();
                for (int i = Math.Min(_anchor, target); i <= Math.Max(_anchor, target); i++)
                {
                    _selected.Add(_tiles[i].Record.Id);
                }

                _focusIndex = target;
                ApplySelection();
            }
            else
            {
                ClickSelect(target, ModifierKeys.None);
            }

            ScrollIntoView(target);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.A when mods.HasFlag(ModifierKeys.Control):
                _selected.UnionWith(_tiles.Select(t => t.Record.Id));
                ApplySelection();
                e.Handled = true;
                break;
            case Key.Enter when _selected.Count == 1:
                _ = _host?.OpenCaptureAsync(_selected.Single());
                e.Handled = true;
                break;
            case Key.F2 when _selected.Count == 1:
                OnRename(this, e);
                e.Handled = true;
                break;
            case Key.Delete when _selected.Count > 0:
                OnDelete(this, e);
                e.Handled = true;
                break;
            case Key.Escape:
                _selected.Clear();
                ApplySelection();
                e.Handled = true;
                break;
        }
    }

    private void ScrollIntoView(int tileIndex)
    {
        if (Rows.ItemsSource is not List<object> rows)
        {
            return;
        }

        for (int r = 0; r < rows.Count; r++)
        {
            if (rows[r] is LibraryTileRow row && row.Tiles.Any(t => t.Index == tileIndex))
            {
                if (Rows.ItemContainerGenerator.ContainerFromIndex(r) is FrameworkElement fe)
                {
                    fe.BringIntoView();
                }
                else if (FindScroller() is { } sv)
                {
                    // Not realized yet (virtualized): approximate by item index.
                    sv.ScrollToVerticalOffset(sv.ScrollableHeight * r / Math.Max(1, rows.Count - 1));
                }

                return;
            }
        }
    }

    private ScrollViewer? FindScroller() =>
        Rows.Template.FindName("RowsScroller", Rows) as ScrollViewer;

    // ------------------------------------------------------------------ actions

    private List<CaptureRecord> SelectedRecords() =>
        [.. _tiles.Where(t => _selected.Contains(t.Record.Id)).Select(t => t.Record)];

    private Window? Owner => Window.GetWindow(this);

    private async Task RunAsync(Action work, string done)
    {
        try
        {
            await _services!.Writer.RunAsync(work).ConfigureAwait(true);
            _host?.SetStatus(done);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or KeyNotFoundException or ArgumentException)
        {
            MessageBox.Show(Owner!, ex.Message, ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Refresh();
    }

    private void OnOpenSelected(object sender, RoutedEventArgs e)
    {
        if (SelectedRecords() is [var r, ..])
        {
            _ = _host?.OpenCaptureAsync(r.Id);
        }
    }

    /// <summary>Exports recordings as MP4 copies (streamed file copy; the video is never loaded into memory).</summary>
    private async Task ExportVideosAsync(List<CaptureRecord> videos)
    {
        string? folder = null;
        if (videos.Count == 1)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export video",
                Filter = "MP4 video (*.mp4)|*.mp4",
                FileName = SnapForge.Core.Export.ExportFileNames.For(videos[0].Title, videos[0].CreatedUtc.ToLocalTime(), ".mp4", new HashSet<string>()),
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            string target = dialog.FileName;
            // FA-008: streamed, hash-verified export (temp + flush + rename); a damaged video is not exported.
            await RunAsync(() => _services!.Library.ExportVideo(videos[0].Id, target), $"Exported {target}").ConfigureAwait(true);
            return;
        }

        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Export videos to folder" };
        if (picker.ShowDialog(Window.GetWindow(this)) == true)
        {
            folder = picker.FolderName;
        }

        if (folder is null)
        {
            return;
        }

        var used = new HashSet<string>(Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        await RunAsync(() =>
        {
            foreach (CaptureRecord v in videos)
            {
                _services!.Library.ExportVideo(v.Id, Path.Combine(folder, SnapForge.Core.Export.ExportFileNames.For(v.Title, v.CreatedUtc.ToLocalTime(), ".mp4", used)));
            }
        }, $"Exported {videos.Count} videos to {folder}").ConfigureAwait(true);
    }

    private async void OnRename(object sender, RoutedEventArgs e)
    {
        if (SelectedRecords() is not [var r])
        {
            return;
        }

        string? title = InputDialog.Ask(Owner, "Rename capture", "New name:", r.Title);
        if (!string.IsNullOrWhiteSpace(title))
        {
            await RunAsync(() => _services!.Library.Rename(r.Id, title), "Renamed.").ConfigureAwait(true);
        }
    }

    private async void OnDuplicate(object sender, RoutedEventArgs e)
    {
        List<CaptureRecord> records = [.. SelectedRecords().Where(r => !r.IsVideo)];
        await RunAsync(() =>
        {
            foreach (CaptureRecord r in records)
            {
                _services!.Library.Duplicate(r.Id, DateTime.UtcNow);
            }
        }, records.Count == 1 ? "Duplicated." : $"Duplicated {records.Count} captures.").ConfigureAwait(true);
    }

    /// <summary>More → Combine images: the selected images (as exported: annotations and redactions applied) placed
    /// side by side or stacked, in the order shown, as a new capture. The originals are not changed.</summary>
    private async void OnCombine(object sender, RoutedEventArgs e)
    {
        List<CaptureRecord> records = [.. SelectedRecords().Where(r => !r.IsVideo && r.Damage is null)];
        if (records.Count < 2)
        {
            _host?.SetStatus("Select two or more images to combine.");
            return;
        }

        var direction = (sender as FrameworkElement)?.Tag as string == "Vertical" ? CombineDirection.Vertical : CombineDirection.Horizontal;
        try
        {
            CaptureRecord created = await _services!.Writer.RunAsync(() =>
            {
                var parts = new List<PixelBuffer>();
                foreach (CaptureRecord r in records)
                {
                    CaptureContent c = _services.Library.Load(r.Id);
                    var doc = new EditorDocument(c.BaseImage, c.Annotations);
                    parts.Add(ImageExporter.Flatten(doc.Image, doc.Annotations));
                    doc.Discard();
                }

                PixelBuffer combined = ImageOps.Combine(parts, direction, 16, ArgbColor.White, CombineAlignment.Center);
                return _services.Library.Create(new CaptureContent(combined, []), combined, DateTime.UtcNow, $"Combined ({records.Count} images)");
            }).ConfigureAwait(true);
            _host?.SetStatus($"Created \"{created.Title}\" ({created.Width} × {created.Height}).");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            _host?.SetStatus($"The images could not be combined: {ex.Message}");
        }
    }

    private async void OnToggleFavorite(object sender, RoutedEventArgs e)
    {
        List<CaptureRecord> records = SelectedRecords();
        bool favorite = records.Any(r => !r.IsFavorite);
        await RunAsync(() =>
        {
            foreach (CaptureRecord r in records)
            {
                _services!.Library.SetFavorite(r.Id, favorite);
            }
        }, favorite ? "Added to favorites." : "Removed from favorites.").ConfigureAwait(true);
    }

    private async void OnEditTags(object sender, RoutedEventArgs e)
    {
        List<CaptureRecord> records = SelectedRecords();
        if (records.Count == 0)
        {
            return;
        }

        string initial = records.Count == 1 ? string.Join(", ", records[0].Meta.Tags) : string.Empty;
        string? text = InputDialog.Ask(Owner, "Tags", records.Count == 1 ? "Tags (comma-separated):" : $"Tags for {records.Count} captures (comma-separated; replaces existing tags):", initial);
        if (text is null)
        {
            return;
        }

        string[] tags = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await RunAsync(() =>
        {
            foreach (CaptureRecord r in records)
            {
                _services!.Library.SetTags(r.Id, tags);
            }
        }, "Tags updated.").ConfigureAwait(true);
    }

    private async void OnSetFolder(object sender, RoutedEventArgs e)
    {
        List<CaptureRecord> records = SelectedRecords();
        if (records.Count == 0)
        {
            return;
        }

        string? folder = InputDialog.Ask(Owner, "Move to folder", "Folder name (leave empty to remove from folders):", records[0].Meta.Folder);
        if (folder is null)
        {
            return;
        }

        await RunAsync(() =>
        {
            foreach (CaptureRecord r in records)
            {
                _services!.Library.SetFolder(r.Id, folder);
            }
        }, folder.Trim().Length == 0 ? "Removed from folder." : $"Moved to \"{folder.Trim()}\".").ConfigureAwait(true);
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        List<CaptureRecord> records = [.. SelectedRecords().Where(r => r.Damage is null)];
        if (records.Count == 0 || _host is null)
        {
            return;
        }

        if (records.Any(r => r.IsVideo))
        {
            await ExportVideosAsync([.. records.Where(r => r.IsVideo)]).ConfigureAwait(true);
            records = [.. records.Where(r => !r.IsVideo)];
            if (records.Count == 0)
            {
                return;
            }
        }

        if (records.Count == 1)
        {
            CaptureRecord r = records[0];
            CaptureContent c = await _services!.Writer.RunAsync(() => _services.Library.Load(r.Id)).ConfigureAwait(true);
            var doc = new EditorDocument(c.BaseImage, c.Annotations);
            PixelBuffer flat = ImageExporter.Flatten(doc.Image, doc.Annotations);
            doc.Discard();
            await _host.ExportImageAsync(flat, r.Title, r.CreatedUtc.ToLocalTime()).ConfigureAwait(true);
            return;
        }

        var dialog = new OpenFolderDialog { Title = $"Export {records.Count} captures as PNG" };
        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        int done = 0;
        foreach (CaptureRecord r in records)
        {
            CaptureContent c = await _services!.Writer.RunAsync(() => _services.Library.Load(r.Id)).ConfigureAwait(true);
            var doc = new EditorDocument(c.BaseImage, c.Annotations);
            PixelBuffer flat = ImageExporter.Flatten(doc.Image, doc.Annotations);
            doc.Discard();
            string name = string.Concat(r.Title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            string path = Path.Combine(dialog.FolderName, name + ".png");
            for (int i = 2; File.Exists(path); i++)
            {
                path = Path.Combine(dialog.FolderName, $"{name} ({i}).png");
            }

            try
            {
                await ImageExporter.SaveAsync(flat, path, Core.Export.ImageFileFormat.Png).ConfigureAwait(true);
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(Owner!, $"Export stopped at \"{r.Title}\":\n\n{ex.Message}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            }
        }

        _host.SetStatus($"Exported {done} capture(s) to {dialog.FolderName}");
    }

    private async void OnExportPackage(object sender, RoutedEventArgs e)
    {
        if (SelectedRecords() is not [var r] || r.IsVideo)
        {
            _host?.SetStatus("Select one image capture to export it as an editable package (videos are exported as MP4).");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export editable capture",
            Filter = $"SnapForge capture (*{CaptureFormat.PackageExtension})|*{CaptureFormat.PackageExtension}",
            FileName = r.Title + CaptureFormat.PackageExtension,
        };
        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        if (r.HasReversibleRedactions && MessageBox.Show(Owner!, "This editable capture still contains the original pixels under its redactions. Anyone you give the .sfcap file to can undo them.\n\nExport anyway?",
                ProductInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAsync(() => _services!.Library.ExportPackage(r.Id, dialog.FileName), $"Exported {dialog.FileName}").ConfigureAwait(true);
    }

    private async void OnSanitize(object sender, RoutedEventArgs e)
    {
        List<CaptureRecord> records = [.. SelectedRecords().Where(r => r.Damage is null && !r.IsVideo)];
        if (SelectedRecords().Any(r => r.IsVideo))
        {
            _host?.SetStatus("Videos cannot be sanitized or redacted; only image captures were included.");
        }

        if (records.Count == 0 || _host is null)
        {
            return;
        }

        if (MessageBox.Show(Owner!, $"Sanitize {records.Count} capture(s)?\n\nRedactions are burned into the stored images, original pixels and hidden annotations are removed from the Library copies, and thumbnails are regenerated. This cannot be undone. Earlier exports and backups are not changed.",
                "Sanitize", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _host.SanitizeRecordsAsync([.. records.Select(r => r.Id)]).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(Owner!, ex.Message, ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Refresh();
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_query.View == Core.Library.LibraryView.Trash)
        {
            OnDeletePermanently(sender, e);
            return;
        }

        Guid[] ids = [.. SelectedRecords().Select(r => r.Id)];
        if (ids.Length > 0 && _host is not null)
        {
            await _host.DeleteCapturesAsync(ids).ConfigureAwait(true);
            _selected.Clear();
            Refresh();
        }
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        Guid[] ids = [.. SelectedRecords().Where(r => r.IsDeleted).Select(r => r.Id)];
        if (ids.Length > 0)
        {
            await RunAsync(() => _services!.Library.Restore(ids), ids.Length == 1 ? "Restored." : $"Restored {ids.Length} captures.").ConfigureAwait(true);
        }
    }

    private async void OnDeletePermanently(object sender, RoutedEventArgs e)
    {
        Guid[] ids = [.. SelectedRecords().Select(r => r.Id)];
        if (ids.Length == 0 || MessageBox.Show(Owner!, $"Permanently delete {ids.Length} capture(s)?\n\nThe files are overwritten and removed and cannot be restored from SnapForge. (Backups, file-recovery tools or SSD wear-levelling may still hold old copies; see Help → Privacy.)",
                ProductInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        _host?.OnCapturesRemoved(ids);
        await RunAsync(() => _services!.Library.DeletePermanently(ids), $"Permanently deleted {ids.Length} capture(s).").ConfigureAwait(true);
        _selected.Clear();
    }

    private async void OnEmptyTrash(object sender, RoutedEventArgs e)
    {
        int count = _services!.Library.Records.Count(r => r.IsDeleted);
        if (count == 0 || MessageBox.Show(Owner!, $"Permanently delete all {count} capture(s) in the Trash?", ProductInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        _host?.OnCapturesRemoved([.. _services.Library.Records.Where(r => r.IsDeleted).Select(r => r.Id)]);
        await RunAsync(() => _services.Library.EmptyTrash(), "Trash emptied.").ConfigureAwait(true);
    }

    private void OnStorageSettings(object sender, RoutedEventArgs e) => App.ShowSettings(Owner);
}
