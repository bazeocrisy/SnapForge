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

    public static IReadOnlyList<int> AllowedDelays { get; } = [0, 1, 2, 3, 5, 10];

    public CaptureOptions Normalized() => this with { DelaySeconds = Math.Clamp(DelaySeconds, 0, 60) };
}

/// <summary>A named, reusable set of capture options with an optional global hotkey.</summary>
public sealed record CapturePreset
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = "Preset";

    public CaptureOptions Options { get; init; } = new();

    public HotkeyGesture? Hotkey { get; init; }

    public static IReadOnlyList<CapturePreset> Defaults { get; } =
    [
        new() { Id = new Guid("8f6d2b0e-1a3c-4d5e-9f00-000000000001"), Name = "Region to Editor", Options = new() { Mode = CaptureMode.Region } },
        new() { Id = new Guid("8f6d2b0e-1a3c-4d5e-9f00-000000000002"), Name = "Window to Editor", Options = new() { Mode = CaptureMode.Window } },
        new() { Id = new Guid("8f6d2b0e-1a3c-4d5e-9f00-000000000003"), Name = "Full screen to Clipboard (no history)", Options = new() { Mode = CaptureMode.AllScreens, PreviewInEditor = false, CopyToClipboard = true, SaveToLibrary = false } },
        new() { Id = new Guid("8f6d2b0e-1a3c-4d5e-9f00-000000000004"), Name = "Menu capture (3 s delay, cursor)", Options = new() { Mode = CaptureMode.Region, DelaySeconds = 3, CaptureCursor = true } },
    ];
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
/// User preferences, stored as JSON in %LOCALAPPDATA%\SnapForge\settings.json. Unknown or damaged files fall back
/// to defaults (the damaged file is kept for inspection).
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

    public bool ShowCaptureWindowOnStart { get; init; }

    /// <summary>Set once the user has seen the storage/privacy notice.</summary>
    public bool PrivacyNoticeShown { get; init; }

    public IReadOnlyList<QuickStyle> UserQuickStyles { get; init; } = [];

    public IReadOnlyList<Guid> FavoriteQuickStyles { get; init; } = [];

    public AnnotationTool LastTool { get; init; } = AnnotationTool.Arrow;

    public ArgbColor LastColor { get; init; } = ArgbColor.Parse("#E53935");

    public double LastStrokeWidth { get; init; } = 4;

    public string EffectiveLibraryPath => string.IsNullOrWhiteSpace(LibraryPath) ? CaptureLibrary.DefaultRoot() : LibraryPath;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "settings.json");

    public IEnumerable<(string Action, HotkeyGesture? Gesture)> HotkeyAssignments() =>
        Presets.Select(p => ($"Preset: {p.Name}", p.Hotkey)).Prepend(("Capture", CaptureHotkey));

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            AppSettings? s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(path), Json);
            if (s is null || s.Version > CurrentVersion)
            {
                throw new JsonException("Unsupported settings version.");
            }

            return s with { Capture = s.Capture.Normalized(), Presets = s.Presets.Count == 0 ? CapturePreset.Defaults : s.Presets };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or ArgumentException or FormatException)
        {
            try
            {
                File.Move(path, path + ".damaged", overwrite: true);
            }
            catch (IOException)
            {
            }

            return new AppSettings();
        }
    }

    public void Save(string path) => AtomicFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(this, Json));

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
