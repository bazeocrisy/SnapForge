using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SnapForge.Core.Annotations;
using SnapForge.Core.Capture;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>What happens when a capture is taken (the Capture window's options).</summary>
public sealed record CaptureOptions
{
    public CaptureMode Mode { get; init; } = CaptureMode.Region;

    public int DelaySeconds { get; init; }

    public bool CaptureCursor { get; init; }

    public bool PreviewInEditor { get; init; } = true;

    public bool CopyToClipboard { get; init; }

    /// <summary>Save to the Capture Library. Off = capture without history (kept in memory only).</summary>
    public bool SaveToLibrary { get; init; } = true;

    /// <summary>Quick choices offered by the delay picker; any whole number of seconds 0–<see cref="MaxDelaySeconds"/> is accepted.</summary>
    public static IReadOnlyList<int> AllowedDelays { get; } = [0, 1, 2, 3, 5, 10, 15, 30, 60];

    public const int MaxDelaySeconds = 60;

    public CaptureOptions Normalized() => this with { DelaySeconds = Math.Clamp(DelaySeconds, 0, MaxDelaySeconds) };
}

/// <summary>A named, reusable set of capture options with an optional global hotkey.</summary>
public sealed record CapturePreset
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = "Preset";

    public CaptureOptions Options { get; init; } = new();

    public HotkeyGesture? Hotkey { get; init; }

    /// <summary>Name of built-in preset 4 before milestone 3. It suggested a menu-capture mode that does not exist.</summary>
    public const string LegacyMenuPresetName = "Menu capture (3 s delay, cursor)";

    public const string DelayedRegionPresetName = "Region with 3 s delay and pointer";

    public static Guid DelayedRegionPresetId { get; } = new("8f6d2b0e-1a3c-4d5e-9f00-000000000004");

    public static IReadOnlyList<CapturePreset> Defaults { get; } =
    [
        new() { Id = new Guid("8f6d2b0e-1a3c-4d5e-9f00-000000000001"), Name = "Region to Editor", Options = new() { Mode = CaptureMode.Region } },
        new() { Id = new Guid("8f6d2b0e-1a3c-4d5e-9f00-000000000002"), Name = "Window to Editor", Options = new() { Mode = CaptureMode.Window } },
        new() { Id = new Guid("8f6d2b0e-1a3c-4d5e-9f00-000000000003"), Name = "Full screen to Clipboard (no history)", Options = new() { Mode = CaptureMode.AllScreens, PreviewInEditor = false, CopyToClipboard = true, SaveToLibrary = false } },
        new() { Id = DelayedRegionPresetId, Name = DelayedRegionPresetName, Options = new() { Mode = CaptureMode.Region, DelaySeconds = 3, CaptureCursor = true } },
    ];
}

/// <summary>File type and quality produced when captures are dragged out of SnapForge.</summary>
public sealed record DragDropSettings
{
    public Export.ImageFileFormat Format { get; init; } = Export.ImageFileFormat.Png;

    public int JpegQuality { get; init; } = Export.ExportRules.JpegQuality;

    public DragDropSettings Normalized() => this with { JpegQuality = Math.Clamp(JpegQuality, 1, 100) };
}

/// <summary>A saved color + thickness combination shown in the Quick Styles panel.</summary>
public sealed record QuickStyle
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    public ArgbColor Color { get; init; } = ArgbColor.Parse("#E53935");

    public double StrokeWidth { get; init; } = 4;

    public bool Favorite { get; init; }

    public bool BuiltIn { get; init; }

    public static IReadOnlyList<QuickStyle> BuiltIns { get; } = Build();

    private static List<QuickStyle> Build()
    {
        (string Name, string Hex)[] colors =
        [
            ("Red", "#E53935"), ("Orange", "#FB8C00"), ("Yellow", "#FDD835"), ("Green", "#43A047"),
            ("Blue", "#1E88E5"), ("Purple", "#8E24AA"), ("Black", "#141414"), ("White", "#FFFFFF"),
        ];
        var list = new List<QuickStyle>();
        int n = 0;
        foreach ((string name, string hex) in colors)
        {
            foreach ((string size, double width) in new[] { ("thin", 2.0), ("bold", 6.0) })
            {
                list.Add(new QuickStyle
                {
                    Id = new Guid($"5a1c0000-0000-4000-8000-{++n:D12}"),
                    Name = $"{name} {size}",
                    Color = ArgbColor.Parse(hex),
                    StrokeWidth = width,
                    BuiltIn = true,
                });
            }
        }

        return list;
    }
}

/// <summary>
/// User preferences, stored as JSON in %LOCALAPPDATA%\SnapForge\settings.json. Damaged files fall back to defaults
/// (a uniquely named copy is kept); files from a newer version load as defaults in read-only mode and are never
/// overwritten (see <see cref="LoadWithStatus"/>).
/// </summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Library folder; null = default (%LOCALAPPDATA%\SnapForge\Library).</summary>
    public string? LibraryPath { get; init; }

    public CaptureOptions Capture { get; init; } = new();

    public IReadOnlyList<CapturePreset> Presets { get; init; } = CapturePreset.Defaults;

    /// <summary>Global hotkey for "capture with the Capture window's current options".</summary>
    public HotkeyGesture? CaptureHotkey { get; init; } = new(HotkeyModifiers.None, "PrintScreen");

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    public bool ShowRecentTray { get; init; } = true;

    public bool ShowPropertiesPanel { get; init; } = true;

    public double PropertiesPanelWidth { get; init; } = 260;

    public int LibraryThumbnailSize { get; init; } = 180;

    public LibrarySort LibrarySort { get; init; } = LibrarySort.NewestFirst;

    public RetentionPolicy Retention { get; init; } = new();

    /// <summary>Closing the main window keeps SnapForge running in the notification area (hotkeys keep working).</summary>
    public bool CloseToNotificationArea { get; init; } = true;

    /// <summary>Show the capture panel at start (capture-first, like a separate capture app). Default on.</summary>
    public bool ShowCaptureWindowOnStart { get; init; } = true;

    /// <summary>Set once the "capture panel at start" default (milestone 3.1) was applied to an older settings file.</summary>
    public bool CapturePanelDefaultApplied { get; init; }

    /// <summary>Keep the capture panel above other windows.</summary>
    public bool CapturePanelTopmost { get; init; }

    /// <summary>Optional global hotkey that shows or hides the capture panel (unassigned by default).</summary>
    public HotkeyGesture? ShowPanelHotkey { get; init; }

    /// <summary>Set once the user has seen the storage/privacy notice.</summary>
    public bool PrivacyNoticeShown { get; init; }

    public IReadOnlyList<QuickStyle> UserQuickStyles { get; init; } = [];

    public IReadOnlyList<Guid> FavoriteQuickStyles { get; init; } = [];

    public AnnotationTool LastTool { get; init; } = AnnotationTool.Arrow;

    public ArgbColor LastColor { get; init; } = ArgbColor.Parse("#E53935");

    public double LastStrokeWidth { get; init; } = 4;

    /// <summary>Desktop capture widget (enabled and topmost by default).</summary>
    public WidgetSettings Widget { get; init; } = new();

    /// <summary>Set once the widget has been shown open on first start (so users notice it).</summary>
    public bool WidgetIntroShown { get; init; }

    /// <summary>What dragging a capture out of SnapForge produces.</summary>
    public DragDropSettings DragDrop { get; init; } = new();

    /// <summary>User's choice for "Launch with Windows" (the registry value is the source of truth at run time).</summary>
    public bool LaunchAtStartup { get; init; }

    /// <summary>Last position of the capture panel; null = centred on the primary monitor.</summary>
    public WindowPosition? CapturePanelPosition { get; init; }

    /// <summary>Screen recording preferences (Milestone V1). Older settings files have none: defaults apply.</summary>
    public VideoSettings Video { get; init; } = new();

    public string EffectiveLibraryPath => string.IsNullOrWhiteSpace(LibraryPath) ? CaptureLibrary.DefaultRoot() : LibraryPath;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "settings.json");

    public IEnumerable<(string Action, HotkeyGesture? Gesture)> HotkeyAssignments() =>
        Presets.Select(p => ($"Preset: {p.Name}", p.Hotkey))
            .Prepend(("Show capture panel", ShowPanelHotkey))
            .Prepend(("Capture", CaptureHotkey))
            .Append(("Record / pause / resume", Video.RecordHotkey))
            .Append(("Stop recording", Video.StopHotkey))
            .Append(("Cancel recording", Video.CancelHotkey));

    /// <summary>Loads settings; see <see cref="LoadWithStatus"/> for what happens with damaged or newer files.</summary>
    public static AppSettings Load(string path) => LoadWithStatus(path).Settings;

    /// <summary>
    /// Loads settings and says how (FA-019):
    /// <list type="bullet">
    /// <item>missing file: defaults (<see cref="SettingsLoadStatus.Missing"/>);</item>
    /// <item>a file written by a newer SnapForge (higher <see cref="Version"/>) or one that cannot be read: defaults in
    /// read-only mode (<see cref="SettingsLoadResult.IsReadOnly"/>). The file is left exactly as it is, and
    /// <see cref="Save"/>/<see cref="TrySave"/> refuse to overwrite a newer file;</item>
    /// <item>a damaged file: defaults, after copying the file to a unique
    /// <c>settings.damaged-&lt;utc&gt;-&lt;random&gt;.json</c> beside it (earlier copies are never overwritten).</item>
    /// </list>
    /// </summary>
    public static SettingsLoadResult LoadWithStatus(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Missing, null, null);
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Unreadable, $"The settings file could not be read ({ex.Message}). Defaults are used and the file is not overwritten.", null);
        }

        if (ProbeVersion(bytes) is int version && version > CurrentVersion)
        {
            return new SettingsLoadResult(
                new AppSettings(),
                SettingsLoadStatus.NewerVersion,
                $"The settings were saved by a newer version of SnapForge (settings version {version}). Defaults are used for this session and the file is not changed.",
                null);
        }

        try
        {
            AppSettings s = JsonSerializer.Deserialize<AppSettings>(bytes, Json) ?? throw new JsonException("The settings file is empty.");
            if (s.Version < 1)
            {
                throw new JsonException($"Invalid settings version {s.Version}.");
            }

            return new SettingsLoadResult(Normalize(s), SettingsLoadStatus.Loaded, null, null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException)
        {
            string? copy = CopyDamaged(path, bytes);
            string message = copy is null
                ? $"The settings file is damaged ({ex.Message}); defaults are used. A copy could not be kept."
                : $"The settings file is damaged ({ex.Message}); defaults are used. A copy was kept as {Path.GetFileName(copy)}.";
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Damaged, message, copy);
        }
    }

    private static AppSettings Normalize(AppSettings s) => s with
    {
        Capture = s.Capture.Normalized(),
        Presets = s.Presets.Count == 0 ? CapturePreset.Defaults : MigratePresets(s.Presets),
        Widget = s.Widget ?? new WidgetSettings(),
        DragDrop = (s.DragDrop ?? new DragDropSettings()).Normalized(),
        Video = (s.Video ?? new VideoSettings()).Normalized(),

        // Milestone 3.1: the capture panel opens at start by default. Older files stored "false" because it
        // was the previous default, so the new default is applied once; the user can turn it off again.
        ShowCaptureWindowOnStart = s.CapturePanelDefaultApplied ? s.ShowCaptureWindowOnStart : true,
        CapturePanelDefaultApplied = true,
    };

    /// <summary>The top-level "Version" number of a settings file, or null if it cannot be determined.</summary>
    private static int? ProbeVersion(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(p.Name, nameof(Version), StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number)
                    {
                        return p.Value.TryGetInt32(out int v) ? v : int.MaxValue;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>Copies a damaged settings file to a new unique name beside it; returns the copy's path or null.</summary>
    private static string? CopyDamaged(string path, byte[] bytes)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(path);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            string random = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
            string copy = Path.Combine(dir, $"{stem}.damaged-{stamp}-{random}.json");
            try
            {
                // CreateNew: an earlier copy is never overwritten.
                using (var fs = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes);
                    fs.Flush(flushToDisk: true);
                }

                return copy;
            }
            catch (IOException) when (File.Exists(copy))
            {
                // Name collision: try another random suffix.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Renames the built-in delayed-region preset if the user never renamed it (its old name implied a menu-capture
    /// mode that SnapForge does not have). User-renamed presets are left alone.
    /// </summary>
    internal static IReadOnlyList<CapturePreset> MigratePresets(IReadOnlyList<CapturePreset> presets) =>
        [.. presets.Select(p => p.Id == CapturePreset.DelayedRegionPresetId && p.Name == CapturePreset.LegacyMenuPresetName
            ? p with { Name = CapturePreset.DelayedRegionPresetName }
            : p)];

    /// <summary>
    /// Saves atomically. Throws <see cref="SettingsSaveException"/> (an <see cref="IOException"/>) when the file
    /// cannot be written, or when the existing file was written by a newer SnapForge, which is never overwritten (FA-019).
    /// </summary>
    public void Save(string path)
    {
        SettingsSaveResult result = TrySave(path);
        if (!result.Succeeded)
        {
            throw new SettingsSaveException(result.Error ?? "The settings could not be saved.", result.Exception);
        }
    }

    /// <summary>Saves atomically and reports failure instead of throwing (FA-019). See <see cref="Save"/>.</summary>
    public SettingsSaveResult TrySave(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (File.Exists(path) && ProbeVersion(File.ReadAllBytes(path)) is int existing && existing > CurrentVersion)
            {
                return new SettingsSaveResult(false, $"The settings file was saved by a newer version of SnapForge (settings version {existing}); it was not overwritten.", null, IsReadOnly: true);
            }

            AtomicFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(this, Json));
            return new SettingsSaveResult(true, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new SettingsSaveResult(false, $"The settings could not be saved: {ex.Message}", ex);
        }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new HotkeyJsonConverter(), new ColorJsonConverter() },
    };

    private sealed class HotkeyJsonConverter : JsonConverter<HotkeyGesture>
    {
        public override HotkeyGesture? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : HotkeyGesture.Parse(reader.GetString());

        public override void Write(Utf8JsonWriter writer, HotkeyGesture value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class ColorJsonConverter : JsonConverter<ArgbColor>
    {
        public override ArgbColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ArgbColor.Parse(reader.GetString() ?? throw new JsonException("Color missing."));

        public override void Write(Utf8JsonWriter writer, ArgbColor value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
