using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SnapForge.Core;
using SnapForge.Core.Storage;

namespace SnapForge.App.Services;

/// <summary>The two SnapForge programs: Capture (SnapForge.exe) and Editor (SnapForgeEditor.exe).</summary>
public enum AppRole
{
    /// <summary>QuickDock, notification-area icon, hotkeys, screen/image capture and recording.</summary>
    Capture,

    /// <summary>Editing, annotation, playback, trimming, Library browsing and drag-out/export.</summary>
    Editor,
}

/// <summary>A request one SnapForge program leaves for the other (see <see cref="Handoff"/>).</summary>
/// <param name="Action">One of the <see cref="Handoff"/> action constants.</param>
/// <param name="Id">The Library item the request is about (only ever an item whose save has completed).</param>
/// <param name="Path">A file the request is about (an editable .sfcap package for captures kept out of the Library).</param>
/// <param name="Mode">Capture mode for <see cref="Handoff.Capture"/> requests.</param>
/// <param name="CreatedUtc">When the request was written.</param>
internal sealed record HandoffRequest(string Action, Guid? Id = null, string? Path = null, string? Mode = null, DateTime CreatedUtc = default);

/// <summary>How a request was delivered.</summary>
internal enum HandoffDelivery
{
    /// <summary>The target program was running and was signalled.</summary>
    Signalled,

    /// <summary>The target program was started; it reads the request at start-up.</summary>
    Started,

    /// <summary>The target program could not be reached or started; the request stays in its inbox for its next start.</summary>
    Queued,
}

/// <summary>
/// Cross-process hand-off between SnapForge Capture and SnapForge Editor.
/// <para>A request is a small JSON file written atomically into the TARGET program's inbox
/// (<c>%LOCALAPPDATA%\SnapForge\Handoff\&lt;program&gt;</c>, per Windows user and per test profile); then the target's
/// single-instance "activate" event is set, or — if it is not running — its executable is started. The target reads
/// every pending request in order and deletes each one after acting on it. A request only ever carries the stable id
/// of a Library item whose save has COMPLETED (or the path of a file already written), so a lost or failed hand-off
/// can never lose a capture: the item is in the Library and can be opened from the Library at any time. Requests that
/// stayed unread for more than a day are discarded rather than acted on unexpectedly.</para>
/// </summary>
internal static class Handoff
{
    internal const string OpenCapture = "open";
    internal const string PlayVideo = "play";
    internal const string EditVideo = "edit-video";
    internal const string ShowEditor = "editor";
    internal const string ShowLibrary = "library";
    internal const string OpenPackage = "open-package";
    internal const string OpenImage = "open-image";
    internal const string Capture = "capture";
    internal const string CapturePanel = "capture-panel";
    internal const string RecordPanel = "record-panel";

    /// <summary><see cref="HandoffRequest.Mode"/> values besides the capture modes.</summary>
    internal const string ScrollingMode = "Scrolling";

    internal const string RepeatMode = "Repeat";

    internal const string EditorExecutable = "SnapForgeEditor.exe";
    internal const string CaptureExecutable = "SnapForge.exe";

    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Test hook: replaces starting the target program (returns true if "started").</summary>
    internal static Func<AppRole, bool>? StartOverride { get; set; }

    /// <summary>Base single-instance name of a program (the data profile is appended, see <see cref="InstanceKey"/>).</summary>
    internal static string InstanceName(AppRole role) => role == AppRole.Editor ? "SnapForge-Editor-SingleInstance-7c1d" : "SnapForge-SingleInstance-7c1d";

    /// <summary>Single-instance key of a program: one per Windows session, program and data profile.</summary>
    internal static string InstanceKey(AppRole role) =>
        ProductInfo.Profile is null ? InstanceName(role) : $"{InstanceName(role)}-p-{ProductInfo.Profile}";

    /// <summary>Test hook: folder that holds the inboxes instead of the data profile's Handoff folder.</summary>
    internal static string? RootOverride { get; set; }

    internal static string InboxDirectory(AppRole role) =>
        System.IO.Path.Combine(RootOverride ?? System.IO.Path.Combine(ProductInfo.DataRoot, "Handoff"), role == AppRole.Editor ? "editor" : "capture");

    /// <summary>Full path of a program's executable next to this one, or null when it is not installed there (tests).</summary>
    internal static string? ProgramPath(AppRole role)
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, role == AppRole.Editor ? EditorExecutable : CaptureExecutable);
        return File.Exists(path) ? path : null;
    }

    /// <summary>True if the program is running for this user and profile.</summary>
    internal static bool IsRunning(AppRole role)
    {
        if (EventWaitHandle.TryOpenExisting($@"Local\{InstanceKey(role)}-activate", out EventWaitHandle? handle))
        {
            handle.Dispose();
            return true;
        }

        return false;
    }

    /// <summary>Writes a request into the target's inbox and returns the file written.</summary>
    internal static string Write(AppRole target, HandoffRequest request)
    {
        string dir = InboxDirectory(target);
        Directory.CreateDirectory(dir);
        string name = $"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}.json";
        string temp = System.IO.Path.Combine(dir, name + ".tmp");
        string final = System.IO.Path.Combine(dir, name);
        File.WriteAllText(temp, JsonSerializer.Serialize(request with { CreatedUtc = DateTime.UtcNow }, Json));
        File.Move(temp, final); // atomic within the folder: the reader never sees a half-written request
        return final;
    }

    /// <summary>Leaves a request for the target program, then signals it or starts it.</summary>
    internal static HandoffDelivery Send(AppRole target, HandoffRequest request)
    {
        Write(target, request);
        if (Signal(target))
        {
            return HandoffDelivery.Signalled;
        }

        return Start(target) ? HandoffDelivery.Started : HandoffDelivery.Queued;
    }

    /// <summary>Sets the target's activate event; false if the target is not running.</summary>
    internal static bool Signal(AppRole target)
    {
        if (!EventWaitHandle.TryOpenExisting($@"Local\{InstanceKey(target)}-activate", out EventWaitHandle? handle))
        {
            return false;
        }

        using (handle)
        {
            handle.Set();
        }

        return true;
    }

    /// <summary>Starts the target program (same data profile); false if it is not installed next to this one.</summary>
    internal static bool Start(AppRole target)
    {
        if (StartOverride is { } start)
        {
            return start(target);
        }

        if (ProgramPath(target) is not { } exe)
        {
            return false;
        }

        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        if (ProductInfo.Profile is { } profile)
        {
            info.ArgumentList.Add("--profile");
            info.ArgumentList.Add(profile);
        }

        try
        {
            using Process? process = Process.Start(info);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Pending requests for <paramref name="role"/>, oldest first. Damaged files and requests older than a day are
    /// deleted without being returned.
    /// </summary>
    internal static IReadOnlyList<(string File, HandoffRequest Request)> Pending(AppRole role)
    {
        string dir = InboxDirectory(role);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var list = new List<(string, HandoffRequest)>();
        foreach (string file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            HandoffRequest? request = null;
            try
            {
                request = JsonSerializer.Deserialize<HandoffRequest>(File.ReadAllText(file), Json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }

            if (request is null || string.IsNullOrEmpty(request.Action) || DateTime.UtcNow - request.CreatedUtc > MaxAge)
            {
                Complete(file);
                continue;
            }

            list.Add((file, request));
        }

        return list;
    }

    /// <summary>Removes a request once it has been acted on.</summary>
    internal static void Complete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Request for an Editor started with command-line arguments (<c>--open &lt;id&gt;</c>, <c>--library</c>, a file path…).</summary>
    internal static HandoffRequest? FromArguments(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--open" when i + 1 < args.Count && Guid.TryParse(args[i + 1], out Guid id):
                    return new HandoffRequest(OpenCapture, id);
                case "--play" when i + 1 < args.Count && Guid.TryParse(args[i + 1], out Guid video):
                    return new HandoffRequest(PlayVideo, video);
                case "--library":
                    return new HandoffRequest(ShowLibrary);
                case var path when path.EndsWith(CaptureFormat.PackageExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(path):
                    return new HandoffRequest(OpenPackage, Path: System.IO.Path.GetFullPath(path));
            }
        }

        return null;
    }
}
