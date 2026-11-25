using System.Text.Json;
using System.Text.Json.Nodes;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Library;

public sealed record TrayItem(Guid Id, bool Pinned);

/// <summary>
/// The Recent Captures Tray state (persisted to tray.json in the library root). Pinned items stay at the left in
/// the order they were pinned; other items are most-recent first and capped at <see cref="MaxUnpinned"/>.
/// Closing an item only removes it from the tray; it never deletes the capture.
/// </summary>
public sealed class RecentTray
{
    public const int MaxUnpinned = 40;
    private readonly string _file;
    private readonly List<TrayItem> _items = [];

    private RecentTray(string file) => _file = file;

    public event EventHandler? Changed;

    public IReadOnlyList<TrayItem> Items => _items;

    public static RecentTray Open(string libraryRoot, Func<Guid, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var tray = new RecentTray(Path.Combine(libraryRoot, "tray.json"));
        try
        {
            if (File.Exists(tray._file) && JsonNode.Parse(File.ReadAllBytes(tray._file)) is JsonObject o && o["items"] is JsonArray arr)
            {
                foreach (JsonNode? n in arr)
                {
                    if (n is JsonObject item && Guid.TryParse(item["id"]?.GetValue<string>(), out Guid id) && exists(id) && tray._items.All(i => i.Id != id))
                    {
                        tray._items.Add(new TrayItem(id, item["pinned"]?.GetValue<bool>() ?? false));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException)
        {
            // A damaged tray file only loses the tray layout; captures are unaffected.
            tray._items.Clear();
        }

        tray.Normalize();
        return tray;
    }

    public bool Contains(Guid id) => _items.Any(i => i.Id == id);

    /// <summary>Adds (or moves) a capture to the front of the unpinned section.</summary>
    public void Add(Guid id)
    {
        int index = _items.FindIndex(i => i.Id == id);
        if (index >= 0 && _items[index].Pinned)
        {
            return;
        }

        if (index >= 0)
        {
            _items.RemoveAt(index);
        }

        _items.Insert(_items.Count(i => i.Pinned), new TrayItem(id, false));
        Commit();
    }

    /// <summary>Removes from the tray only (the capture stays in the Library).</summary>
    public void Close(Guid id)
    {
        if (_items.RemoveAll(i => i.Id == id) > 0)
        {
            Commit();
        }
    }

    public void CloseAllUnpinned()
    {
        if (_items.RemoveAll(i => !i.Pinned) > 0)
        {
            Commit();
        }
    }

    public void SetPinned(Guid id, bool pinned)
    {
        int index = _items.FindIndex(i => i.Id == id);
        if (index < 0 || _items[index].Pinned == pinned)
        {
            return;
        }

        _items.RemoveAt(index);
        int pinnedCount = _items.Count(i => i.Pinned);
        _items.Insert(pinned ? pinnedCount : pinnedCount, new TrayItem(id, pinned));
        Commit();
    }

    /// <summary>Drops items whose capture no longer exists (or is in the Trash).</summary>
    public void Prune(Func<Guid, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        if (_items.RemoveAll(i => !exists(i.Id)) > 0)
        {
            Commit();
        }
    }

    private void Normalize()
    {
        var pinned = _items.Where(i => i.Pinned).ToList();
        var unpinned = _items.Where(i => !i.Pinned).Take(MaxUnpinned).ToList();
        _items.Clear();
        _items.AddRange(pinned);
        _items.AddRange(unpinned);
    }

    private void Commit()
    {
        Normalize();
        var arr = new JsonArray();
        foreach (TrayItem i in _items)
        {
            arr.Add(new JsonObject { ["id"] = i.Id.ToString("D"), ["pinned"] = i.Pinned });
        }

        AtomicFile.WriteAllBytes(_file, JsonSerializer.SerializeToUtf8Bytes(new JsonObject { ["format"] = "snapforge.tray", ["version"] = 1, ["items"] = arr }));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
