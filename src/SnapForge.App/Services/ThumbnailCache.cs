using System.Windows.Media.Imaging;
using SnapForge.App.Rendering;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;

namespace SnapForge.App.Services;

/// <summary>
/// Bounded in-memory cache of library thumbnails. Thumbnails are decoded on a background thread on first request
/// (lazy loading) and frozen so they can cross threads; least-recently-used entries are evicted.
/// </summary>
internal sealed class ThumbnailCache
{
    public const int Capacity = 400;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, LinkedListNode<(Guid Id, BitmapSource Image)>> _map = [];
    private readonly LinkedList<(Guid Id, BitmapSource Image)> _lru = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public BitmapSource? TryGet(Guid id)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(id, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Image;
            }
        }

        return null;
    }

    public async Task<BitmapSource?> GetAsync(CaptureLibrary library, Guid id)
    {
        if (TryGet(id) is { } hit)
        {
            return hit;
        }

        BitmapSource? image = await Task.Run(() =>
        {
            PixelBuffer? pixels = library.LoadThumbnail(id);
            return pixels is null ? null : BitmapConversion.ToFrozenBitmap(pixels);
        }).ConfigureAwait(true);
        if (image is not null)
        {
            Put(id, image);
        }

        return image;
    }

    public void Put(Guid id, BitmapSource image)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(id, out var existing))
            {
                _lru.Remove(existing);
            }

            _map[id] = _lru.AddFirst((id, image));
            while (_map.Count > Capacity && _lru.Last is { } last)
            {
                _map.Remove(last.Value.Id);
                _lru.RemoveLast();
            }
        }
    }

    public void Invalidate(Guid id)
    {
        lock (_gate)
        {
            if (_map.Remove(id, out var node))
            {
                _lru.Remove(node);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
        }
    }
}
