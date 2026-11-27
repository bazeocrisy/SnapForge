using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SnapForge.App.Rendering;
using SnapForge.Core.Export;
using SnapForge.Core.Imaging;
using SnapForge.Core.Settings;

namespace SnapForge.App.Services;

/// <summary>One capture offered in a drag: how to get its flattened (export-safe) pixels.</summary>
/// <param name="Key">Library id or in-memory session key.</param>
/// <param name="Title">Used for the file name.</param>
/// <param name="CreatedLocal">Used for the file name's timestamp.</param>
/// <param name="RenderFlattened">
/// Returns the image exactly as File → Export would write it: annotations drawn, redactions burned in. Called on
/// the UI thread only if a drop target asks for files.
/// </param>
internal sealed record DragCapture(Guid Key, string Title, DateTime CreatedLocal, Func<PixelBuffer> RenderFlattened)
{
    /// <summary>For a recording: the Library MP4, copied (streamed) into the drag folder — never offered in place.</summary>
    public string? VideoSource { get; init; }
}

/// <summary>
/// Dragging captures out of SnapForge (Recent Captures tray and Library) to Explorer, the desktop, Office, mail,
/// chat and browsers.
/// <para>
/// Privacy: a drag is an export. Files are produced by the same flatten + encode path as File → Export (no
/// metadata), into a private per-drag folder under %LOCALAPPDATA%\SnapForge\Temp\Drag. Editable packages, base
/// images and original pixels under redactions are never offered. Files are only written when a target actually
/// asks for them (so reordering inside the tray never creates files) and are removed after a grace period, on exit
/// and at the next start.
/// </para>
/// </summary>
internal static class CaptureDragDrop
{
    /// <summary>Private format carrying the dragged keys (used for tray reordering and to ignore our own drops).</summary>
    public const string KeysFormat = "SnapForge.CaptureKeys";

    private const string PreferredDropEffectFormat = "Preferred DropEffect";

    public static TempExportStore Store { get; set; } = new(TempExportStore.DefaultRoot());

    /// <summary>Test hook: observes the data object right before the modal drag loop starts.</summary>
    internal static Action<LazyCaptureFiles>? BeforeDragHook { get; set; }

    /// <summary>True while the modal OLE drag loop runs (tests use it as a watchdog signal).</summary>
    internal static bool IsDragging { get; private set; }

    /// <summary>
    /// Tooltips are not opened while a SnapForge drag runs. During the modal OLE loop WPF no longer sees mouse
    /// moves, so the drag source's hover timer keeps running and its tooltip can open mid-drag; a tooltip is a
    /// topmost window, and if it lies under the pointer OLE treats it as the window there — the real target gets
    /// DragLeave and the drop is lost (intermittent CI failures of the multi-selection drag, runs 111–125; see
    /// DEFECT-REGISTER DEF-RC2-DND-01). Internal switch so a test can demonstrate the mechanism.
    /// </summary>
    internal static bool SuppressToolTipsDuringDrag { get; set; } = true;

    private static readonly bool ToolTipSuppressionRegistered = RegisterToolTipSuppression();

    private static bool RegisterToolTipSuppression()
    {
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent, new ToolTipEventHandler((_, e) =>
        {
            if (IsDragging && SuppressToolTipsDuringDrag)
            {
                e.Handled = true; // cancels the opening
            }
        }));
        return true;
    }

    /// <summary>Test hook: called with the effect the target chose when the OLE drag loop has ended.</summary>
    internal static Action<DragDropEffects>? AfterDragHook { get; set; }

    /// <summary>The last drag's data object (tests inspect what a target would receive).</summary>
    internal static LazyCaptureFiles? LastDrag { get; private set; }

    /// <summary>Starts a modal OLE drag. Returns the effect the target chose (None = cancelled / refused).</summary>
    public static DragDropEffects StartDrag(DependencyObject source, IReadOnlyList<DragCapture> items, DragDropSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return DragDropEffects.None;
        }

        _ = ToolTipSuppressionRegistered;
        var data = new LazyCaptureFiles(items, settings ?? new DragDropSettings(), Store);
        LastDrag = data;
        BeforeDragHook?.Invoke(data);
        DragDropEffects effect = DragDropEffects.None;
        IsDragging = true;
        try
        {
            // Copy only: the target gets copies; nothing in SnapForge is moved or deleted by a drop.
            effect = DragDrop.DoDragDrop(source, new DataObject(data), DragDropEffects.Copy);
        }
        finally
        {
            IsDragging = false;
            data.EndDrag(effect != DragDropEffects.None);
            AfterDragHook?.Invoke(effect);
        }

        return effect;
    }

    /// <summary>True if the data came from a SnapForge drag (so a drop onto SnapForge is a reorder, not an import).</summary>
    public static bool IsOwnDrag(IDataObject data) => data?.GetDataPresent(KeysFormat) == true;

    /// <summary>Keys carried by a SnapForge drag.</summary>
    public static IReadOnlyList<Guid> KeysOf(IDataObject data) =>
        data?.GetData(KeysFormat) is string s ? [.. s.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse)] : [];

    /// <summary>Applies the drag-copy retention policy (start-up and after each drag).</summary>
    public static void Sweep() => Store.Enforce(DateTime.UtcNow, TempExportStore.MaxTotalBytes);

    /// <summary>
    /// A data object whose file list is produced on first request. WPF's DataObject wraps it and converts the
    /// string[] to CF_HDROP for native targets.
    /// </summary>
    internal sealed class LazyCaptureFiles(IReadOnlyList<DragCapture> items, DragDropSettings settings, TempExportStore store) : IDataObject
    {
        private string[]? _files;

        public IReadOnlyList<DragCapture> Items { get; } = items;

        /// <summary>Folder holding this drag's files (null until a target requested them).</summary>
        public string? SessionDirectory { get; private set; }

        public bool FilesCreated => _files is not null;

        /// <summary>Why files could not be produced (disk full, damaged capture), if that happened.</summary>
        public string? Error { get; private set; }

        public object? GetData(string format) => GetData(format, autoConvert: true);

        public object? GetData(Type format) => format == typeof(string[]) ? GetData(DataFormats.FileDrop) : null;

        public object? GetData(string format, bool autoConvert)
        {
            if (format == KeysFormat)
            {
                return string.Join(';', Items.Select(i => i.Key.ToString("D")));
            }

            if (format == PreferredDropEffectFormat)
            {
                return new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy));
            }

            if (format == DataFormats.FileDrop || (autoConvert && format == "FileNameW"))
            {
                try
                {
                    string[] files = EnsureFiles();
                    return format == DataFormats.FileDrop ? files : files.Take(1).ToArray();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
                {
                    // Never let an exception unwind into the OLE drag loop; the target simply gets no files.
                    Error = ex.Message;
                    return null;
                }
            }

            return null;
        }

        public bool GetDataPresent(string format) => GetFormats().Contains(format);

        public bool GetDataPresent(Type format) => format == typeof(string[]);

        public bool GetDataPresent(string format, bool autoConvert) => GetDataPresent(format);

        public string[] GetFormats() => GetFormats(autoConvert: true);

        public string[] GetFormats(bool autoConvert) => [DataFormats.FileDrop, KeysFormat, PreferredDropEffectFormat];

        public void SetData(object data) => throw new NotSupportedException();

        public void SetData(string format, object data) => throw new NotSupportedException();

        public void SetData(Type format, object data) => throw new NotSupportedException();

        public void SetData(string format, object data, bool autoConvert) => throw new NotSupportedException();

        /// <summary>Writes the flattened exports (once) and returns their full paths.</summary>
        internal string[] EnsureFiles()
        {
            if (_files is not null)
            {
                return _files;
            }

            string dir = store.NewSession(DateTime.UtcNow);
            SessionDirectory = dir;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();
            try
            {
                foreach (DragCapture item in Items)
                {
                    if (item.VideoSource is { } video)
                    {
                        string mp4 = Path.Combine(dir, ExportFileNames.For(item.Title, item.CreatedLocal, ".mp4", used));
                        File.Copy(video, mp4);
                        paths.Add(mp4);
                        continue;
                    }

                    PixelBuffer flat = item.RenderFlattened();
                    string path = Path.Combine(dir, ExportFileNames.For(item.Title, item.CreatedLocal, settings.Format, used));
                    File.WriteAllBytes(path, ImageExporter.Encode(flat, settings.Format, settings.JpegQuality));
                    paths.Add(path);
                }
            }
            catch
            {
                TempExportStore.DeleteSession(dir);
                SessionDirectory = null;
                throw;
            }

            _files = [.. paths];
            return _files;
        }

        /// <summary>
        /// After the drag loop. No drop: the files (if any were made) are deleted now. A drop: the files stay, because
        /// the receiving app may read them later (lazy uploads, attachments); the retention policy removes them after
        /// 24 hours or earlier under the size cap — see <see cref="TempExportStore"/>.
        /// </summary>
        internal void EndDrag(bool dropped)
        {
            if (SessionDirectory is not { } dir)
            {
                return;
            }

            if (!dropped)
            {
                TempExportStore.DeleteSession(dir);
                SessionDirectory = null;
                return;
            }

            TempExportStore.MarkDropped(dir, DateTime.UtcNow); // FA-009: protection now runs from the drop time
            _ = Task.Run(Sweep);
        }

        /// <summary>File drop list as a StringCollection (what WinForms/WPF targets read).</summary>
        internal StringCollection FileDropList()
        {
            var list = new StringCollection();
            list.AddRange(EnsureFiles());
            return list;
        }
    }
}
