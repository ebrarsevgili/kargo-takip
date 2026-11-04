using KargoTakip.Api.Models;

namespace KargoTakip.Api.Storage;

public sealed class JsonVehicleRepository : IVehicleRepository
{
    private readonly JsonCollection<Vehicle> _items;

    public JsonVehicleRepository(StorageOptions options) =>
        _items = new(Path.Combine(options.DataDirectory, "vehicles.json"), v => v.Id.ToString());

    public Task<IReadOnlyList<Vehicle>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Vehicle>>(_items.Where().OrderBy(v => v.Plate).ToList());

    public Task<Vehicle?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_items.Get(id.ToString()));

    public Task<Vehicle?> GetByPlateAsync(string normalizedPlate, CancellationToken ct = default) =>
        Task.FromResult(_items.Find(v => v.Plate == normalizedPlate));

    public Task AddAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        if (!_items.TryAdd(vehicle)) throw new InvalidOperationException("A vehicle with this id already exists.");
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        if (!_items.Replace(vehicle)) throw new KeyNotFoundException("Vehicle not found.");
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_items.Remove(id.ToString()));
}

public sealed class JsonCourierRepository : ICourierRepository
{
    private readonly JsonCollection<Courier> _items;

    public JsonCourierRepository(StorageOptions options) =>
        _items = new(Path.Combine(options.DataDirectory, "couriers.json"), c => c.Id.ToString());

    public Task<IReadOnlyList<Courier>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Courier>>(_items.Where().OrderBy(c => c.Name).ToList());

    public Task<Courier?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_items.Get(id.ToString()));

    public Task AddAsync(Courier courier, CancellationToken ct = default)
    {
        if (!_items.TryAdd(courier)) throw new InvalidOperationException("A courier with this id already exists.");
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Courier courier, CancellationToken ct = default)
    {
        if (!_items.Replace(courier)) throw new KeyNotFoundException("Courier not found.");
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_items.Remove(id.ToString()));
}
