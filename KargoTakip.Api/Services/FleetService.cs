using System.Text.RegularExpressions;
using KargoTakip.Api.Models;
using KargoTakip.Api.Security;
using KargoTakip.Api.Storage;

namespace KargoTakip.Api.Services;

/// <summary>Vehicles and couriers. Everything here is admin-only.</summary>
public sealed partial class FleetService
{
    /// <summary>Stored in English; the UI shows Turkish labels.</summary>
    public static readonly IReadOnlyList<string> VehicleTypes = ["motorbike", "car", "van", "truck"];

    private readonly IVehicleRepository _vehicles;
    private readonly ICourierRepository _couriers;
    private readonly IUserRepository _users;
    private readonly IShipmentRepository _shipments;
    private readonly AuthService _auth;

    public FleetService(IVehicleRepository vehicles, ICourierRepository couriers, IUserRepository users,
        IShipmentRepository shipments, AuthService auth)
    {
        _vehicles = vehicles;
        _couriers = couriers;
        _users = users;
        _shipments = shipments;
        _auth = auth;
    }

    // Turkish plate: province code 01-81, 1-3 letters, 2-4 digits (e.g. 34 ABC 123).
    [GeneratedRegex(@"^(0[1-9]|[1-7]\d|8[01])[A-Z]{1,3}\d{2,4}$")]
    private static partial Regex PlateFormat();

    public static string NormalizePlate(string plate) =>
        string.Concat(plate.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    private static ServiceResult<T>? Guard<T>(Actor actor) =>
        actor.Role == UserRole.Admin ? null : ServiceResult<T>.Forbidden("Bu işlem yalnızca yöneticiler içindir.");

    // =====================================================================
    // Vehicles
    // =====================================================================

    public async Task<ServiceResult<IReadOnlyList<VehicleDto>>> ListVehiclesAsync(Actor actor, CancellationToken ct)
    {
        if (Guard<IReadOnlyList<VehicleDto>>(actor) is { } denied) return denied;

        var vehicles = await _vehicles.ListAsync(ct);
        var couriers = await _couriers.ListAsync(ct);
        return ServiceResult<IReadOnlyList<VehicleDto>>.Ok(vehicles.Select(v => ToDto(v, couriers)).ToList());
    }

    public async Task<ServiceResult<VehicleDto>> CreateVehicleAsync(Actor actor, VehicleRequest req, CancellationToken ct)
    {
        if (Guard<VehicleDto>(actor) is { } denied) return denied;

        var errors = ValidateVehicle(req, out var plate, out var type);
        if (errors.Count > 0) return ServiceResult<VehicleDto>.Invalid(errors);

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            if (await _vehicles.GetByPlateAsync(plate, ct) is not null)
                return ServiceResult<VehicleDto>.Conflict("Bu plakayla kayıtlı bir araç zaten var.");

            var vehicle = new Vehicle { Plate = plate, Type = type, CapacityKg = Math.Round(req.CapacityKg!.Value, 1) };
            await _vehicles.AddAsync(vehicle, ct);
            return ServiceResult<VehicleDto>.Ok(ToDto(vehicle, []));
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    public async Task<ServiceResult<VehicleDto>> UpdateVehicleAsync(Actor actor, Guid id, VehicleRequest req, CancellationToken ct)
    {
        if (Guard<VehicleDto>(actor) is { } denied) return denied;

        var errors = ValidateVehicle(req, out var plate, out var type);
        if (errors.Count > 0) return ServiceResult<VehicleDto>.Invalid(errors);

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            var vehicle = await _vehicles.GetAsync(id, ct);
            if (vehicle is null) return ServiceResult<VehicleDto>.NotFound();

            if (await _vehicles.GetByPlateAsync(plate, ct) is { } other && other.Id != id)
                return ServiceResult<VehicleDto>.Conflict("Bu plakayla kayıtlı başka bir araç var.");

            vehicle.Plate = plate;
            vehicle.Type = type;
            vehicle.CapacityKg = Math.Round(req.CapacityKg!.Value, 1);
            await _vehicles.UpdateAsync(vehicle, ct);

            return ServiceResult<VehicleDto>.Ok(ToDto(vehicle, await _couriers.ListAsync(ct)));
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    public async Task<ServiceResult<bool>> DeleteVehicleAsync(Actor actor, Guid id, CancellationToken ct)
    {
        if (Guard<bool>(actor) is { } denied) return denied;

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            if (await _vehicles.GetAsync(id, ct) is null) return ServiceResult<bool>.NotFound();

            if ((await _couriers.ListAsync(ct)).Any(c => c.VehicleId == id))
                return ServiceResult<bool>.Conflict("Bu araç bir kuryeye atanmış. Önce kuryenin aracını değiştir.");

            await _vehicles.DeleteAsync(id, ct);
            return ServiceResult<bool>.Ok(true);
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    private static Dictionary<string, string[]> ValidateVehicle(VehicleRequest req, out string plate, out string type)
    {
        var errors = new Dictionary<string, string[]>();
        plate = string.IsNullOrWhiteSpace(req.Plate) ? "" : NormalizePlate(req.Plate);
        type = req.Type?.Trim().ToLowerInvariant() ?? "";

        if (!PlateFormat().IsMatch(plate))
            errors[nameof(req.Plate)] = ["Plaka geçerli değil. Örnek: 34 ABC 123"];

        if (!VehicleTypes.Contains(type))
            errors[nameof(req.Type)] = [$"Araç tipi şunlardan biri olmalı: {string.Join(", ", VehicleTypes)}."];

        if (req.CapacityKg is null or <= 0 or > 50_000)
            errors[nameof(req.CapacityKg)] = ["Kapasite 0'dan büyük ve en fazla 50000 kg olmalı."];

        return errors;
    }

    private static VehicleDto ToDto(Vehicle v, IReadOnlyList<Courier> couriers)
    {
        var courier = couriers.FirstOrDefault(c => c.VehicleId == v.Id);
        return new VehicleDto(v.Id, v.Plate, v.Type, v.CapacityKg, courier?.Id, courier?.Name);
    }
}
