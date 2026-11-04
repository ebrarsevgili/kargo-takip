using KargoTakip.Api.Models;

namespace KargoTakip.Api.Storage;

/// <summary>Where the JSON files live. One file per kind of data (shipments.json, users.json, ...).</summary>
public record StorageOptions(string DataDirectory);

public sealed class JsonShipmentRepository : IShipmentRepository
{
    private readonly JsonCollection<Shipment> _items;

    public JsonShipmentRepository(StorageOptions options) =>
        _items = new(Path.Combine(options.DataDirectory, "shipments.json"), s => s.TrackingNumber);

    public Task<IReadOnlyList<Shipment>> ListAsync(ShipmentStatus? status, Guid? ownerUserId, Guid? courierId, CancellationToken ct = default)
    {
        IReadOnlyList<Shipment> result = _items
            .Where(s => (status is null || s.Status == status)
                     && (ownerUserId is null || s.OwnerUserId == ownerUserId)
                     && (courierId is null || s.CourierId == courierId))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<Shipment?> GetAsync(string trackingNumber, CancellationToken ct = default) =>
        Task.FromResult(_items.Get(trackingNumber));

    public Task<bool> ExistsAsync(string trackingNumber, CancellationToken ct = default) =>
        Task.FromResult(_items.Exists(trackingNumber));

    public Task AddAsync(Shipment shipment, CancellationToken ct = default)
    {
        if (!_items.TryAdd(shipment)) throw new InvalidOperationException($"{shipment.TrackingNumber} tracking number already exists.");
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Shipment shipment, CancellationToken ct = default)
    {
        if (!_items.Replace(shipment)) throw new KeyNotFoundException($"{shipment.TrackingNumber} not found.");
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string trackingNumber, CancellationToken ct = default) =>
        Task.FromResult(_items.Remove(trackingNumber));
}
