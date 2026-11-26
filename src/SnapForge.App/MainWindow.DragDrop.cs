using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SnapForge.App.Rendering;
using SnapForge.App.Services;
using SnapForge.App.Views;
using SnapForge.Core.Editing;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.App;

/// <summary>Drag and drop: captures out of the tray/Library, tray reordering, and image files in.</summary>
public partial class MainWindow
{
    private Point? _trayDragStart;
    private TrayTileViewModel? _trayDragTile;
    private int _trayAnchor = -1;

    // ------------------------------------------------------------------ drag out

    /// <summary>
    /// What a drag of <paramref name="key"/> exports: the open editor session's current state if the capture is
    /// open (including unsaved edits), otherwise the Library copy — always flattened with redactions burned in.
    /// </summary>
    internal DragCapture? DragItemFor(Guid key)
    {
        if (_sessions.TryGetValue(key, out EditorSession? session) && !session.Document.IsDiscarded)
        {
            return new DragCapture(key, session.Title, session.CapturedAt, () => ImageExporter.Flatten(session.Document.Image, session.Document.Annotations));
        }

        if (_services.Library.Find(key) is { IsDeleted: false, Damage: null } record)
        {
            return new DragCapture(key, record.Title, record.CreatedUtc.ToLocalTime(), () =>
            {
                CaptureContent content = _services.Library.Load(key);
                var doc = new EditorDocument(content.BaseImage, content.Annotations);
                try
                {
                    return ImageExporter.Flatten(doc.Image, doc.Annotations);
                }
                finally
                {
                    doc.Discard();
                }
            });
        }

        return null;
    }

    /// <summary>Starts a drag of the given captures and reports the outcome in the status bar.</summary>
    internal DragDropEffects StartCaptureDrag(DependencyObject source, IEnumerable<Guid> keys)
    {
        List<DragCapture> items = [.. keys.Select(DragItemFor).OfType<DragCapture>()];
        if (items.Count == 0)
        {
            return DragDropEffects.None;
        }

        DragDropEffects effect = CaptureDragDrop.StartDrag(source, items, _services.Settings.DragDrop);
        if (CaptureDragDrop.LastDrag is { Error: { } error })
        {
            SetStatus($"Drag failed: {error}");
        }
        else if (effect != DragDropEffects.None && CaptureDragDrop.LastDrag is { FilesCreated: true })
        {
            string format = _services.Settings.DragDrop.Format == Core.Export.ImageFileFormat.Jpeg ? "JPEG" : "PNG";
            SetStatus(items.Count == 1 ? $"Copied \"{items[0].Title}\" as a {format} image." : $"Copied {items.Count} captures as {format} images.");
        }

        return effect;
    }

    private void OnTrayTilePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _trayDragStart = e.GetPosition(this);
        _trayDragTile = TileFrom(sender);
    }

    private void OnTrayTilePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _trayDragStart is not { } start || _trayDragTile is not { } tile)
        {
            return;
        }

        Vector d = e.GetPosition(this) - start;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _trayDragStart = null;
        _trayDragTile = null;
        (sender as UIElement)?.ReleaseMouseCapture();
        List<Guid> keys = tile.IsSelected && _trayTiles.Count(t => t.IsSelected) > 1
            ? [.. _trayTiles.Where(t => t.IsSelected).Select(t => t.Key)]
            : [tile.Key];
        StartCaptureDrag((DependencyObject)sender, keys);
        e.Handled = true;
    }

    /// <summary>
    /// Tray clicks: Ctrl toggles selection, Shift selects a range (for multi-capture drags); a plain click selects
    /// the tile and opens the capture.
    /// </summary>
    private async Task OnTrayTileClickedAsync(TrayTileViewModel tile)
    {
        int index = _trayTiles.IndexOf(tile);
        ModifierKeys mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control))
        {
            tile.IsSelected = !tile.IsSelected;
            _trayAnchor = index;
            return;
        }

        if (mods.HasFlag(ModifierKeys.Shift) && _trayAnchor >= 0 && _trayAnchor < _trayTiles.Count)
        {
            int lo = Math.Min(_trayAnchor, index), hi = Math.Max(_trayAnchor, index);
            for (int i = 0; i < _trayTiles.Count; i++)
            {
                _trayTiles[i].IsSelected = i >= lo && i <= hi;
            }

            return;
        }

        foreach (TrayTileViewModel t in _trayTiles)
        {
            t.IsSelected = ReferenceEquals(t, tile);
        }

        _trayAnchor = index;
        await OpenTileAsync(tile).ConfigureAwait(true);
    }

    /// <summary>Selects the first <paramref name="count"/> tray tiles (screenshots and tests).</summary>
    internal IReadOnlyList<Guid> SelectTrayTiles(int count)
    {
        for (int i = 0; i < _trayTiles.Count; i++)
        {
            _trayTiles[i].IsSelected = i < count;
        }

        return [.. _trayTiles.Where(t => t.IsSelected).Select(t => t.Key)];
    }

    /// <summary>Tests turn this off so a failed import does not open a modal message box.</summary>
    internal bool ShowImportErrors { get; set; } = true;

    /// <summary>The on-screen element of a tray tile (tests).</summary>
    internal FrameworkElement? TrayTileElement(Guid key) =>
        _trayTiles.FirstOrDefault(t => t.Key == key) is { } tile ? TrayItems.ItemContainerGenerator.ContainerFromItem(tile) as FrameworkElement : null;

    /// <summary>Tooltip text of a tray tile (tests).</summary>
    internal string TrayTooltip(Guid key) => _trayTiles.FirstOrDefault(t => t.Key == key)?.Tooltip ?? string.Empty;

    /// <summary>Closes every open editor session, keeping captures in the Library and tray (tests).</summary>
    internal async Task CloseAllSessionsForTestAsync()
    {
        foreach (EditorSession s in _sessions.Values.ToList())
        {
            await CloseSessionAsync(s, removeFromTray: false).ConfigureAwait(true);
        }
    }

    /// <summary>Current tray tiles in display order (tests).</summary>
    internal IReadOnlyList<Guid> TrayKeys => [.. _trayTiles.Select(t => t.Key)];

    // ------------------------------------------------------------------ drag the open capture from the Editor

    private Point? _editorDragStart;

    private void OnDragOutMouseDown(object sender, MouseButtonEventArgs e) => _editorDragStart = e.GetPosition(this);

    private void OnDragOutMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _editorDragStart is not { } start || _current is null)
        {
            return;
        }

        Vector d = e.GetPosition(this) - start;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _editorDragStart = null;
        Editor.CommitTextEdit();
        (sender as UIElement)?.ReleaseMouseCapture();
        StartCaptureDrag((DependencyObject)sender, [_current.Key]);
        e.Handled = true;
    }

    private void OnDragOutClick(object sender, RoutedEventArgs e)
    {
        _editorDragStart = null;
        SetStatus("Press and drag the Drag button into another app (Explorer, Word, Outlook, Teams, a browser…) to copy this capture as an image.");
    }

    private void OnShowWidget(object sender, RoutedEventArgs e) => App.ShowWidget();

    // ------------------------------------------------------------------ tray reordering (drop onto the tray)

    private void OnTrayDragOver(object sender, DragEventArgs e)
    {
        if (CaptureDragDrop.IsOwnDrag(e.Data))
        {
            IReadOnlyList<Guid> keys = CaptureDragDrop.KeysOf(e.Data);
            // Reorder needs exactly one Library capture that is in the tray. Copy is the only effect a SnapForge
            // drag allows, so it is also the feedback here; no file is created for an in-tray drop.
            e.Effects = keys.Count == 1 && _services.Tray.Contains(keys[0]) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void OnTrayDrop(object sender, DragEventArgs e)
    {
        if (!CaptureDragDrop.IsOwnDrag(e.Data))
        {
            return; // external files: handled by the window (import)
        }

        e.Handled = true;
        IReadOnlyList<Guid> keys = CaptureDragDrop.KeysOf(e.Data);
        if (keys is [Guid key] && _services.Tray.Contains(key))
        {
            MoveTrayItem(key, e.GetPosition(TrayItems).X);
        }
    }

    /// <summary>Moves a tray capture so it lands at horizontal position <paramref name="x"/> (tray coordinates).</summary>
    internal void MoveTrayItem(Guid key, double x)
    {
        int target = 0;
        foreach (TrayTileViewModel t in _trayTiles.Where(t => !t.NotInLibrary && t.Key != key))
        {
            if (TrayItems.ItemContainerGenerator.ContainerFromItem(t) is FrameworkElement c && c.IsLoaded)
            {
                double centre = c.TranslatePoint(new Point(c.ActualWidth / 2, 0), TrayItems).X;
                if (centre < x)
                {
                    target++;
                }
            }
        }

        if (_services.Tray.Move(key, target))
        {
            SetStatus("Tray order changed.");
        }
    }

    // ------------------------------------------------------------------ image files in (import)

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        e.Effects = !CaptureDragDrop.IsOwnDrag(e.Data) && ImportablePaths(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Handled || CaptureDragDrop.IsOwnDrag(e.Data))
        {
            return;
        }

        e.Handled = true;
        IReadOnlyList<string> paths = ImportablePaths(e.Data);
        if (paths.Count > 0)
        {
            await ImportFilesAsync(paths, openFirst: EditorTab.IsChecked == true).ConfigureAwait(true);
        }
    }

    private static IReadOnlyList<string> ImportablePaths(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files
            ? [.. files.Where(ImageImport.IsSupportedPath)]
            : [];

    private async void OnImportImages(object sender, RoutedEventArgs e) => await ImportWithDialogAsync().ConfigureAwait(true);

    /// <summary>File → Import images… and the Library's Import button.</summary>
    internal async Task ImportWithDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import images into the Library",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.jpe;*.bmp",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ImportFilesAsync(dialog.FileNames, openFirst: false).ConfigureAwait(true);
        }
    }

    /// <summary>Imports files as new Library captures; reports failures; optionally opens the first one.</summary>
    internal async Task<IReadOnlyList<ImportOutcome>> ImportFilesAsync(IReadOnlyList<string> paths, bool openFirst)
    {
        IReadOnlyList<ImportOutcome> results = await ImageFileImporter.ImportAsync(_services, paths).ConfigureAwait(true);
        List<ImportOutcome> ok = [.. results.Where(r => r.Record is not null)];
        List<ImportOutcome> failed = [.. results.Where(r => r.Record is null)];
        foreach (ImportOutcome r in ok)
        {
            _services.Tray.Add(r.Record!.Id);
        }

        SetStatus(failed.Count == 0
            ? $"Imported {ok.Count} image(s) into the Library."
            : $"Imported {ok.Count} image(s); {failed.Count} could not be imported.");
        if (failed.Count > 0 && ShowImportErrors)
        {
            MessageBox.Show(this, string.Join("\n", failed.Select(f => $"{Path.GetFileName(f.Path)}: {f.Error}")), "Some images could not be imported", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (openFirst && ok.Count > 0)
        {
            await OpenCaptureAsync(ok[0].Record!.Id).ConfigureAwait(true);
        }

        return results;
    }
}
