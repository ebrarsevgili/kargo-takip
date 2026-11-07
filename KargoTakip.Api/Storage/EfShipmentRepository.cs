using KargoTakip.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KargoTakip.Api.Storage;

/// <summary>
/// Shipments in SQLite. Every call opens its own short-lived DbContext (the repository itself is a singleton), and reads
/// are "no tracking": the services keep the object, change it and hand it back to UpdateAsync.
/// The movements are an owned collection, so EF loads them together with the shipment without any Include.
/// Sorting is done in memory because SQLite cannot sort DateTimeOffset values in SQL.
/// </summary>
public sealed class EfShipmentRepository(IDbContextFactory<AppDbContext> factory) : IShipmentRepository
{
    public async Task<IReadOnlyList<Shipment>> ListAsync(ShipmentStatus? status, Guid? ownerUserId, Guid? courierId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        IQueryable<Shipment> query = db.Shipments.AsNoTracking();
        if (status is { } wantedStatus) query = query.Where(s => s.Status == wantedStatus);
        if (ownerUserId is { } owner) query = query.Where(s => s.OwnerUserId == owner);
        if (courierId is { } courier) query = query.Where(s => s.CourierId == courier);

        var items = await query.ToListAsync(ct);
        foreach (var item in items) SortEvents(item);
        return items.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public async Task<Shipment?> GetAsync(string trackingNumber, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var shipment = await db.Shipments.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TrackingNumber == trackingNumber, ct);
        if (shipment is not null) SortEvents(shipment);
        return shipment;
    }

    public async Task<bool> ExistsAsync(string trackingNumber, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Shipments.AnyAsync(s => s.TrackingNumber == trackingNumber, ct);
    }

    public async Task AddAsync(Shipment shipment, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Shipment shipment, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Shipments
            .FirstOrDefaultAsync(s => s.TrackingNumber == shipment.TrackingNumber, ct)
            ?? throw new KeyNotFoundException($"{shipment.TrackingNumber} not found.");

        db.Entry(existing).CurrentValues.SetValues(shipment);   // copies every plain column
        existing.Events.Clear();                                // the movements are replaced as a whole
        foreach (var e in shipment.Events)
            existing.Events.Add(new TrackingEvent { Status = e.Status, Location = e.Location, Note = e.Note, Timestamp = e.Timestamp });

        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(string trackingNumber, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Shipments
            .FirstOrDefaultAsync(s => s.TrackingNumber == trackingNumber, ct);
        if (existing is null) return false;

        db.Shipments.Remove(existing);   // the owned movements are removed with it
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Movements come back oldest first, the order the UI and the services expect.
    private static void SortEvents(Shipment shipment) =>
        shipment.Events = shipment.Events.OrderBy(e => e.Timestamp).ToList();
}
