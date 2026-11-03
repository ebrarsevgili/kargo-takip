using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using KargoTakip.Api.Models;

namespace KargoTakip.Api.Storage;

public record StorageOptions(string FilePath);

/// <summary>
/// Keeps shipments in memory and writes them to a JSON file after every change (first to a temp file, then by
/// replacing the old one, so an interrupted write never corrupts the existing file). Fine for small data sets.
/// </summary>
public sealed class JsonShipmentRepository : IShipmentRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Keep Turkish characters readable in the file (Ş instead of an escape sequence). This output is stored as a file, never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly Dictionary<string, Shipment> _items = new(StringComparer.OrdinalIgnoreCase);

    public JsonShipmentRepository(StorageOptions options)
    {
        _filePath = options.FilePath;
        Load();
    }

    public Task<IReadOnlyList<Shipment>> ListAsync(ShipmentStatus? status, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Shipment> result = _items.Values
                .Where(s => status is null || s.Status == status)
                .OrderByDescending(s => s.CreatedAt)
                .Select(Clone)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<Shipment?> GetAsync(string trackingNumber, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_items.TryGetValue(trackingNumber, out var s) ? Clone(s) : null);
        }
    }

    public Task<bool> ExistsAsync(string trackingNumber, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_items.ContainsKey(trackingNumber));
        }
    }

    public Task AddAsync(Shipment shipment, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_items.TryAdd(shipment.TrackingNumber, Clone(shipment)))
                throw new InvalidOperationException($"{shipment.TrackingNumber} tracking number already exists.");
            Persist();
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Shipment shipment, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_items.ContainsKey(shipment.TrackingNumber))
                throw new KeyNotFoundException($"{shipment.TrackingNumber} not found.");
            _items[shipment.TrackingNumber] = Clone(shipment);
            Persist();
        }
        return Task.CompletedTask;
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            using var stream = File.OpenRead(_filePath);
            var list = JsonSerializer.Deserialize<List<Shipment>>(stream, JsonOptions) ?? new();
            foreach (var s in list) _items[s.TrackingNumber] = s;
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

    private static Shipment Clone(Shipment s) => new()
    {
        TrackingNumber = s.TrackingNumber,
        SenderName = s.SenderName,
        ReceiverName = s.ReceiverName,
        ReceiverPhone = s.ReceiverPhone,
        OriginCity = s.OriginCity,
        DestinationCity = s.DestinationCity,
        WeightKg = s.WeightKg,
        Status = s.Status,
        CreatedAt = s.CreatedAt,
        Events = s.Events.Select(e => new TrackingEvent
        {
            Status = e.Status,
            Location = e.Location,
            Note = e.Note,
            Timestamp = e.Timestamp
        }).ToList()
    };
}
