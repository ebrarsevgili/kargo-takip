using KargoTakip.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KargoTakip.Api.Storage;

/// <summary>User accounts in SQLite. User names are stored in lower case, so a case-insensitive lookup is a plain comparison.</summary>
public sealed class EfUserRepository(IDbContextFactory<AppDbContext> factory) : IUserRepository
{
    public async Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        var name = username.Trim().ToLowerInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == name, ct);
    }

    public async Task<AppUser?> GetByCourierIdAsync(Guid courierId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.CourierId == courierId, ct);
    }

    public async Task<bool> AnyAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AnyAsync(ct);
    }

    public async Task AddAsync(AppUser user, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == user.Id, ct)) throw new KeyNotFoundException("User not found.");

        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(ct) > 0;
    }
}
