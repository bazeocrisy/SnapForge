using System.IO;
using System.Windows;
using SnapForge.App.Services;
using SnapForge.Core;
using SnapForge.Core.Capture;
using SnapForge.Core.Editing;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using SnapForge.Core.Storage;

namespace SnapForge.App;

/// <summary>
/// SnapForge runs as two programs built from the same code (RC3): <b>SnapForge Capture</b> (SnapForge.exe —
/// QuickDock, notification-area icon, hotkeys, capture and recording) and <b>SnapForge Editor</b>
/// (SnapForgeEditor.exe — editing, playback, trimming, Library, drag-out/export). Each has its own single instance per
/// Windows user and data profile; they share the user's Library (see docs/LIBRARY-COORDINATION.md) and talk through
/// <see cref="Handoff"/> requests.
/// </summary>
#pragma warning disable CA1001 // The single-instance handles live for the whole process and are disposed in OnExit.
public partial class App
#pragma warning restore CA1001
{
    /// <summary>Which program this process is. Set by the executable's entry point before the application starts.</summary>
    public static AppRole Role { get; set; } = AppRole.Capture;

    /// <summary>Display name of this program.</summary>
    internal static string ProgramName => Role == AppRole.Editor ? "SnapForge Editor" : "SnapForge Capture";

    /// <summary>Title of the Editor window ("SnapForge Editor" in the Editor program).</summary>
    internal static string EditorTitle => Role == AppRole.Editor ? "SnapForge Editor" : ProductInfo.Name;

    private static bool _processingInbox;

    // ------------------------------------------------------------------ Editor start-up

    private void StartEditor(AppServices services, string[] args)
    {
        var window = new MainWindow(services)
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/SnapForge;component/Assets/SnapForgeEditor.ico", UriKind.Absolute)),
        };
        MainWindow = window;
        window.RestoreToolSettings();
        window.Closed += (_, _) => Current.Shutdown();
        _ = services.StartIndexingAsync();

        if (Handoff.FromArguments(args) is { } initial)
        {
            Handoff.Write(AppRole.Editor, initial);
        }

        window.Show();
        window.ShowLibraryWorkspace();
        window.Activate();
        Dispatcher.BeginInvoke(async () =>
        {
            await services.Indexing.ConfigureAwait(true);
            ProcessInbox();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>The activate event was set: a second launch or the other program left a request.</summary>
    private static void OnActivated()
    {
        if (ProcessInbox())
        {
            return;
        }

        if (Role == AppRole.Editor)
        {
            MainShell?.BringToFront();
        }
        else
        {
            ShowPrimaryWindow();
        }
    }

    /// <summary>Acts on every pending request for this program; true if there was at least one.</summary>
    internal static bool ProcessInbox()
    {
        if (_processingInbox)
        {
            return true;
        }

        _processingInbox = true;
        try
        {
            IReadOnlyList<(string File, HandoffRequest Request)> pending = Handoff.Pending(Role);
            foreach ((string file, HandoffRequest request) in pending)
            {
                try
                {
                    if (Role == AppRole.Editor)
                    {
                        _ = DispatchEditorAsync(request);
                    }
                    else
                    {
                        DispatchCapture(request);
                    }
                }
                finally
                {
                    // The request only points at something already saved; it is not needed once acted on.
                    Handoff.Complete(file);
                }
            }

            return pending.Count > 0;
        }
        finally
        {
            _processingInbox = false;
        }
    }

    /// <summary>
    /// Backup, restore and moving the Library change or read the whole Library for a long time; while one runs the
    /// other SnapForge program could not save (its save would wait for the Library and might time out), and after a
    /// move it would still use the old location. These run only while the other program is closed. Returns true when
    /// it is closed (or the user closed it); otherwise explains what to do.
    /// </summary>
    internal static bool OtherProgramClosedFor(Window? owner, string what)
    {
        AppRole other = Role == AppRole.Editor ? AppRole.Capture : AppRole.Editor;
        while (Handoff.IsRunning(other))
        {
            string name = other == AppRole.Editor ? "SnapForge Editor" : "SnapForge Capture";
            string how = other == AppRole.Editor
                ? "Close SnapForge Editor (save your edits first; they are saved automatically)."
                : "Stop any recording, then exit SnapForge Capture: right-click the SnapForge icon in the notification area → Exit SnapForge Capture.";
            string text = $"To {what}, {name} must be closed first, so nothing is saved to the Library while this runs.\n\n{how}\n\nClick Retry when it is closed.";
            MessageBoxResult answer = owner is null
                ? MessageBox.Show(text, ProgramName, MessageBoxButton.OKCancel, MessageBoxImage.Information)
                : MessageBox.Show(owner, text, ProgramName, MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (answer != MessageBoxResult.OK)
            {
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------------ requests

    /// <summary>
    /// Shows something in SnapForge Editor: in this process when it has the Editor window (the Editor program, tests),
    /// otherwise by handing the request to the Editor program (started if needed). The item is already saved.
    /// </summary>
    internal static void OpenInEditor(HandoffRequest request)
    {
        if (MainShell is not null)
        {
            _ = DispatchEditorAsync(request);
            return;
        }

        HandoffDelivery delivery;
        try
        {
            delivery = Handoff.Send(AppRole.Editor, request);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            delivery = HandoffDelivery.Queued;
        }

        if (delivery == HandoffDelivery.Queued)
        {
            NotifyBalloon("SnapForge Editor could not be opened",
                request.Id is null ? "Open SnapForge Editor from the Start menu." : "The capture is saved in your Library. Open SnapForge Editor from the Start menu to see it.",
                warning: true);
        }
    }

    /// <summary>Asks SnapForge Capture to capture, record or show its panel (in this process when this is Capture).</summary>
    internal static void AskCapture(HandoffRequest request)
    {
        if (Role == AppRole.Capture)
        {
            DispatchCapture(request);
            return;
        }

        if (Handoff.Send(AppRole.Capture, request) == HandoffDelivery.Queued)
        {
            MessageBox.Show("SnapForge Capture is not running and could not be started. Start it from the Start menu (SnapForge Capture), then try again.",
                ProgramName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static void DispatchCapture(HandoffRequest request)
    {
        AppServices? services = AppServices.Current;
        switch (request.Action)
        {
            case Handoff.Capture when request.Mode == Handoff.ScrollingMode:
                _ = CaptureScrollingAsync();
                break;
            case Handoff.Capture when request.Mode == Handoff.RepeatMode:
                _ = RepeatLastCaptureAsync();
                break;
            case Handoff.Capture when services is not null:
                CaptureOptions options = services.Settings.Capture;
                if (Enum.TryParse(request.Mode, out CaptureMode mode))
                {
                    options = options with { Mode = mode };
                }

                _ = CaptureWithOptionsAsync(options);
                break;
            case Handoff.RecordPanel:
                Video.RecordingController.OpenStudio();
                break;
            default:
                ShowCaptureWindow();
                break;
        }
    }

    private static async Task DispatchEditorAsync(HandoffRequest request)
    {
        if (MainShell is not { } main || AppServices.Current is not { } services)
        {
            return;
        }

        main.BringToFront();
        switch (request.Action)
        {
            case Handoff.ShowLibrary:
                main.ShowLibraryWorkspace();
                return;
            case Handoff.ShowEditor:
                main.ShowEditorWorkspace();
                return;
            case Handoff.OpenImage when request.Path is { } path:
                OpenHandedOverImage(main, services, path, request.CreatedUtc);
                return;
            case Handoff.OpenPackage when request.Path is { } package:
                try
                {
                    CaptureRecord imported = await services.Writer.RunAsync(() => services.Library.ImportPackage(package)).ConfigureAwait(true);
                    main.ShowEditorWorkspace();
                    await main.OpenCaptureAsync(imported.Id).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    MessageBox.Show(main, $"The editable capture could not be opened:\n{ex.Message}", ProgramName, MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return;
        }

        if (request.Id is not { } id)
        {
            return;
        }

        CaptureRecord? record = await FindSavedAsync(services, id).ConfigureAwait(true);
        if (record is null || record.IsDeleted)
        {
            MessageBox.Show(main, "That capture is no longer in the Library (it may have been deleted or moved to the Trash).", ProgramName, MessageBoxButton.OK, MessageBoxImage.Information);
            main.ShowLibraryWorkspace();
            return;
        }

        if (record.IsVideo)
        {
            if (request.Action == Handoff.EditVideo)
            {
                new Video.VideoEditorWindow(services, record, services.Library.VideoPath(id)).Show();
            }
            else
            {
                ShowVideo(id);
            }

            return;
        }

        main.ShowEditorWorkspace();
        await main.OpenCaptureAsync(id).ConfigureAwait(true);
    }

    /// <summary>
    /// Finds a Library item, re-reading it from disk when this process has not seen it yet (it was saved by the other
    /// program after this one indexed the Library).
    /// </summary>
    private static async Task<CaptureRecord?> FindSavedAsync(AppServices services, Guid id)
    {
        await services.Indexing.ConfigureAwait(true);
        if (services.Library.Find(id) is { } known)
        {
            return known;
        }

        return await services.Writer.RunAsync(() => services.Library.RefreshFromDisk(id)).ConfigureAwait(true);
    }

    private static void OpenHandedOverImage(MainWindow main, AppServices services, string path, DateTime capturedUtc)
    {
        try
        {
            PixelBuffer image = PixelCodec.Decode(File.ReadAllBytes(path));
            DateTime local = capturedUtc == default ? DateTime.Now : capturedUtc.ToLocalTime();
            main.AcceptCapture(image, local, services.Settings.Capture with { SaveToLibrary = false, CopyToClipboard = false, PreviewInEditor = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(main, $"The capture could not be opened:\n{ex.Message}", ProgramName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // ------------------------------------------------------------------ capture intake (Capture program)

    /// <summary>
    /// What happens to a new capture in SnapForge Capture (no Editor window in this process): saved to the Library
    /// first when "Save to Library" is on, copied to the clipboard when asked, and opened in SnapForge Editor when
    /// "Preview in Editor" is on — by its Library id once the save has COMPLETED, or, when the user keeps captures out
    /// of the Library, as a private temporary file the Editor opens as an unsaved capture and then deletes.
    /// </summary>
    internal static async Task AcceptCaptureInCaptureProgramAsync(AppServices services, PixelBuffer image, DateTime capturedAt, CaptureOptions options)
    {
        CaptureRecord? record = null;
        string? error = null;
        if (options.SaveToLibrary)
        {
            try
            {
                DateTime created = capturedAt.ToUniversalTime();
                record = await services.Writer.RunAsync(() => services.Library.Create(new CaptureContent(image, []), image, created)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                error = ex.Message;
            }
        }

        if (options.CopyToClipboard)
        {
            try
            {
                await Rendering.ImageExporter.CopyToClipboardAsync(image).ConfigureAwait(true);
            }
#pragma warning disable CA1031 // A clipboard failure is reported, never fatal.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                error ??= $"The image could not be copied to the clipboard ({ex.Message}).";
            }
        }

        bool preview = options.PreviewInEditor || (!options.SaveToLibrary && !options.CopyToClipboard) || error is not null;
        if (preview)
        {
            if (record is not null)
            {
                OpenInEditor(new HandoffRequest(Handoff.OpenCapture, record.Id));
            }
            else
            {
                string dir = Path.Combine(ProductInfo.DataRoot, "Temp", "Handoff");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"{Guid.NewGuid():N}.sfpx");
                await File.WriteAllBytesAsync(file, PixelCodec.Encode(image)).ConfigureAwait(true);
                OpenInEditor(new HandoffRequest(Handoff.OpenImage, Path: file));
            }
        }

        if (error is not null)
        {
            NotifyBalloon("SnapForge Capture", $"The capture could not be saved to the Library: {error} It was opened in SnapForge Editor instead.", warning: true);
        }
        else if (!preview)
        {
            NotifyBalloon("SnapForge Capture", options switch
            {
                { SaveToLibrary: true, CopyToClipboard: true } => "Capture saved to the Library and copied to the clipboard.",
                { SaveToLibrary: true } => "Capture saved to the Library.",
                _ => "Capture copied to the clipboard (not saved to the Library).",
            });
        }
    }
}
