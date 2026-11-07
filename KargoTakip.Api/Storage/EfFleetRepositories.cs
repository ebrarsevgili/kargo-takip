using KargoTakip.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KargoTakip.Api.Storage;

/// <summary>Vehicles in SQLite.</summary>
public sealed class EfVehicleRepository(IDbContextFactory<AppDbContext> factory) : IVehicleRepository
{
    public async Task<IReadOnlyList<Vehicle>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var items = await db.Vehicles.AsNoTracking().ToListAsync(ct);
        return items.OrderBy(v => v.Plate).ToList();   // same order as the JSON storage
    }

    public async Task<Vehicle?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<Vehicle?> GetByPlateAsync(string normalizedPlate, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Plate == normalizedPlate, ct);
    }

    public async Task AddAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Vehicles.AnyAsync(v => v.Id == vehicle.Id, ct)) throw new KeyNotFoundException("Vehicle not found.");

        db.Vehicles.Update(vehicle);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Vehicles.Where(v => v.Id == id).ExecuteDeleteAsync(ct) > 0;
    }
}

/// <summary>Couriers in SQLite.</summary>
public sealed class EfCourierRepository(IDbContextFactory<AppDbContext> factory) : ICourierRepository
{
    public async Task<IReadOnlyList<Courier>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var items = await db.Couriers.AsNoTracking().ToListAsync(ct);
        return items.OrderBy(c => c.Name).ToList();
    }

    public async Task<Courier?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Couriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task AddAsync(Courier courier, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Couriers.Add(courier);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Courier courier, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Couriers.AnyAsync(c => c.Id == courier.Id, ct)) throw new KeyNotFoundException("Courier not found.");

        db.Couriers.Update(courier);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Couriers.Where(c => c.Id == id).ExecuteDeleteAsync(ct) > 0;
    }
}
