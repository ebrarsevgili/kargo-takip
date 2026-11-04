using KargoTakip.Api.Models;

namespace KargoTakip.Api.Storage;

public interface IVehicleRepository
{
    Task<IReadOnlyList<Vehicle>> ListAsync(CancellationToken ct = default);
    Task<Vehicle?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Vehicle?> GetByPlateAsync(string normalizedPlate, CancellationToken ct = default);
    Task AddAsync(Vehicle vehicle, CancellationToken ct = default);
    Task UpdateAsync(Vehicle vehicle, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface ICourierRepository
{
    Task<IReadOnlyList<Courier>> ListAsync(CancellationToken ct = default);
    Task<Courier?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Courier courier, CancellationToken ct = default);
    Task UpdateAsync(Courier courier, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
