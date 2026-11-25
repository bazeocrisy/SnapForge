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

    public required Guid Key { get; init; }

    /// <summary>True for a capture taken without history (only in memory).</summary>
    public bool NotInLibrary { get; init; }

    public string Title { get => _title; set { if (Set(ref _title, value)) OnPropertyChanged(nameof(Tooltip)); } }

    public ImageSource? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }

    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }

    public bool IsUnsaved { get => _isUnsaved; set => Set(ref _isUnsaved, value); }

    public bool Pinned { get => _pinned; set { if (Set(ref _pinned, value)) OnPropertyChanged(nameof(PinHeader)); } }

    public string PinHeader => Pinned ? "Un_pin" : "_Pin to tray";

    public string Tooltip => NotInLibrary ? $"{Title} — not saved to the Library" : Title;
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
