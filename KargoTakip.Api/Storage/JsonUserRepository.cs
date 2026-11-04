using KargoTakip.Api.Models;

namespace KargoTakip.Api.Storage;

public sealed class JsonUserRepository : IUserRepository
{
    private readonly JsonCollection<AppUser> _items;

    public JsonUserRepository(StorageOptions options) =>
        _items = new(Path.Combine(options.DataDirectory, "users.json"), u => u.Id.ToString());

    public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_items.Get(id.ToString()));

    public Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        Task.FromResult(_items.Find(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)));

    public Task<AppUser?> GetByCourierIdAsync(Guid courierId, CancellationToken ct = default) =>
        Task.FromResult(_items.Find(u => u.CourierId == courierId));

    public Task<bool> AnyAsync(CancellationToken ct = default) => Task.FromResult(_items.Any());

    public Task AddAsync(AppUser user, CancellationToken ct = default)
    {
        if (!_items.TryAdd(user)) throw new InvalidOperationException("A user with this id already exists.");
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        if (!_items.Replace(user)) throw new KeyNotFoundException("User not found.");
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_items.Remove(id.ToString()));
}
