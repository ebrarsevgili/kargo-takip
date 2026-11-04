using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KargoTakip.Api.Storage;

/// <summary>
/// A small thread-safe collection persisted to one JSON file. Everything is kept in memory and the file is rewritten
/// after every change (first to a temp file, then by replacing the old one, so an interrupted write never corrupts
/// the existing file). Items are cloned on the way in and out, so callers cannot change stored data by accident.
/// Fine for small data sets and learning projects.
/// </summary>
public sealed class JsonCollection<T> where T : class
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Keep Turkish characters readable in the file instead of \u escape sequences (the file is never embedded in HTML).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly Func<T, string> _keyOf;
    private readonly object _gate = new();
    private readonly Dictionary<string, T> _items = new(StringComparer.OrdinalIgnoreCase);

    public JsonCollection(string filePath, Func<T, string> keyOf)
    {
        _filePath = filePath;
        _keyOf = keyOf;
        Load();
    }

    public IReadOnlyList<T> Where(Func<T, bool>? filter = null)
    {
        lock (_gate) return _items.Values.Where(i => filter is null || filter(i)).Select(Clone).ToList();
    }

    public T? Get(string key)
    {
        lock (_gate) return _items.TryGetValue(key, out var item) ? Clone(item) : null;
    }

    public T? Find(Func<T, bool> predicate)
    {
        lock (_gate)
        {
            var item = _items.Values.FirstOrDefault(predicate);
            return item is null ? null : Clone(item);
        }
    }

    public bool Any()
    {
        lock (_gate) return _items.Count > 0;
    }

    public bool Exists(string key)
    {
        lock (_gate) return _items.ContainsKey(key);
    }

    /// <summary>Returns false (and stores nothing) when the key already exists.</summary>
    public bool TryAdd(T item)
    {
        lock (_gate)
        {
            if (!_items.TryAdd(_keyOf(item), Clone(item))) return false;
            Persist();
            return true;
        }
    }

    /// <summary>Returns false when no item with that key exists.</summary>
    public bool Replace(T item)
    {
        lock (_gate)
        {
            var key = _keyOf(item);
            if (!_items.ContainsKey(key)) return false;
            _items[key] = Clone(item);
            Persist();
            return true;
        }
    }

    public bool Remove(string key)
    {
        lock (_gate)
        {
            if (!_items.Remove(key)) return false;
            Persist();
            return true;
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            using var stream = File.OpenRead(_filePath);
            foreach (var item in JsonSerializer.Deserialize<List<T>>(stream, JsonOptions) ?? new())
                _items[_keyOf(item)] = item;
        }
        catch (JsonException ex)
        {
            // Fail loudly instead of overwriting a corrupt file with empty data and losing records.
            throw new InvalidOperationException(
                $"Could not read data file ({_filePath}). Check the file or restore it from a backup.", ex);
        }
    }

    private void Persist()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items.Values.ToList(), JsonOptions));
        File.Move(tmp, _filePath, overwrite: true);
    }

    private static T Clone(T item) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(item, JsonOptions), JsonOptions)!;
}
