using KargoTakip.Api.Models;

namespace KargoTakip.Api.Storage;

/// <summary>
/// Storage contract. A JSON file is used for now; moving to Entity Framework Core later only needs a new
/// class that implements this interface and a changed registration in Program.cs.
/// Returned objects are copies: call UpdateAsync to persist any change you make.
/// </summary>
public interface IShipmentRepository
{
    Task<IReadOnlyList<Shipment>> ListAsync(ShipmentStatus? status, CancellationToken ct = default);
    Task<Shipment?> GetAsync(string trackingNumber, CancellationToken ct = default);
    Task<bool> ExistsAsync(string trackingNumber, CancellationToken ct = default);
    Task AddAsync(Shipment shipment, CancellationToken ct = default);
    Task UpdateAsync(Shipment shipment, CancellationToken ct = default);
}
