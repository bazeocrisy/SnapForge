using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SnapForge.App.Rendering;
using SnapForge.Core.Settings;

namespace SnapForge.App.Views;

internal abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One thumbnail in the Recent Captures Tray.</summary>
internal sealed class TrayTileViewModel : ObservableObject
{
    private ImageSource? _thumbnail;
    private string _title = string.Empty;
    private bool _isCurrent;
    private bool _isUnsaved;
    private bool _pinned;
    private bool _isSelected;
    private string _detail = string.Empty;

    public required Guid Key { get; init; }

    /// <summary>True for a capture taken without history (only in memory).</summary>
    public bool NotInLibrary { get; init; }

    public string Title { get => _title; set { if (Set(ref _title, value)) OnPropertyChanged(nameof(Tooltip)); } }

    public ImageSource? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }

    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }

    public bool IsUnsaved { get => _isUnsaved; set => Set(ref _isUnsaved, value); }

    private bool _isVideo;
    private string _durationText = string.Empty;

    /// <summary>True for screen recordings (play badge and duration on the tile).</summary>
    public bool IsVideo { get => _isVideo; set => Set(ref _isVideo, value); }

    public string DurationText { get => _durationText; set => Set(ref _durationText, value); }

    public bool Pinned { get => _pinned; set { if (Set(ref _pinned, value)) OnPropertyChanged(nameof(PinHeader)); } }

    public string PinHeader => Pinned ? "Un_pin" : "_Pin to tray";

    /// <summary>Selected for a multi-capture drag (Ctrl/Shift-click).</summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Date and size shown in the tooltip.</summary>
    public string Detail { get => _detail; set { if (Set(ref _detail, value)) OnPropertyChanged(nameof(Tooltip)); } }

    public string Tooltip => NotInLibrary
        ? $"{Title} — not saved to the Library\nDrag to another app to copy it as an image"
        : $"{Title}{(Detail.Length > 0 ? "\n" + Detail : string.Empty)}\nDrag to another app to copy it {(IsVideo ? "as an MP4 video" : "as an image")}";
}

/// <summary>One Quick Style tile.</summary>
internal sealed class QuickStyleViewModel(QuickStyle style, bool favorite) : ObservableObject
{
    public QuickStyle Style { get; } = style;

    public string Name => Style.Name;

    public double StrokeWidth => Math.Max(2, Style.StrokeWidth);

    public Brush Brush { get; } = Freeze(new SolidColorBrush(BitmapConversion.ToMediaColor(style.Color)));

    public bool Favorite { get; } = favorite;

    public string FavoriteHeader => Favorite ? "Remove from favorites" : "Add to favorites";

    public bool CanDelete => !Style.BuiltIn;

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
