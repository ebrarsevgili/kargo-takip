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

    // =====================================================================
    // Couriers
    // =====================================================================

    public async Task<ServiceResult<IReadOnlyList<CourierDto>>> ListCouriersAsync(Actor actor, CancellationToken ct)
    {
        if (Guard<IReadOnlyList<CourierDto>>(actor) is { } denied) return denied;

        return ServiceResult<IReadOnlyList<CourierDto>>.Ok(await BuildCourierDtosAsync(await _couriers.ListAsync(ct), ct));
    }

    public async Task<ServiceResult<CourierDto>> CreateCourierAsync(Actor actor, CreateCourierRequest req, CancellationToken ct)
    {
        if (Guard<CourierDto>(actor) is { } denied) return denied;

        var errors = ValidateCourier(req.Name, req.Phone, out var name, out var phone);
        if (errors.Count > 0) return ServiceResult<CourierDto>.Invalid(errors);

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            if (await CheckVehicleAsync<CourierDto>(req.VehicleId, null, ct) is { } vehicleProblem)
                return vehicleProblem;

            // The courier's login is created first: it validates the username and password and nothing is stored if they are wrong.
            var courier = new Courier { Name = name, Phone = phone, VehicleId = req.VehicleId };
            var account = await _auth.CreateUserAsync(req.Username, name, req.Password, UserRole.Courier, courier.Id, ct);
            if (account.Kind != ResultKind.Ok) return Relay<CourierDto>(account);

            try
            {
                await _couriers.AddAsync(courier, ct);
            }
            catch
            {
                await _users.DeleteAsync(account.Value!.Id, ct);   // do not leave a login without a courier behind
                throw;
            }

            return ServiceResult<CourierDto>.Ok((await BuildCourierDtosAsync([courier], ct))[0]);
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    public async Task<ServiceResult<CourierDto>> UpdateCourierAsync(Actor actor, Guid id, UpdateCourierRequest req, CancellationToken ct)
    {
        if (Guard<CourierDto>(actor) is { } denied) return denied;

        var errors = ValidateCourier(req.Name, req.Phone, out var name, out var phone);
        if (errors.Count > 0) return ServiceResult<CourierDto>.Invalid(errors);

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            var courier = await _couriers.GetAsync(id, ct);
            if (courier is null) return ServiceResult<CourierDto>.NotFound();

            if (await CheckVehicleAsync<CourierDto>(req.VehicleId, id, ct) is { } vehicleProblem)
                return vehicleProblem;

            courier.Name = name;
            courier.Phone = phone;
            courier.VehicleId = req.VehicleId;
            await _couriers.UpdateAsync(courier, ct);

            if (await _users.GetByCourierIdAsync(id, ct) is { } user)
            {
                user.DisplayName = name;
                await _users.UpdateAsync(user, ct);
            }

            return ServiceResult<CourierDto>.Ok((await BuildCourierDtosAsync([courier], ct))[0]);
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    /// <summary>
    /// Removes the courier and their login. Refused while they still have open shipments (reassign those first);
    /// delivered / cancelled shipments simply lose the courier reference.
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteCourierAsync(Actor actor, Guid id, CancellationToken ct)
    {
        if (Guard<bool>(actor) is { } denied) return denied;

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            if (await _couriers.GetAsync(id, ct) is null) return ServiceResult<bool>.NotFound();

            var shipments = await _shipments.ListAsync(null, null, id, ct);
            if (shipments.Any(s => !s.IsClosed))
                return ServiceResult<bool>.Conflict("Bu kuryenin açık kargoları var. Önce onları başka bir kuryeye ata veya iptal et.");

            foreach (var shipment in shipments)
            {
                shipment.CourierId = null;
                await _shipments.UpdateAsync(shipment, ct);
            }

            if (await _users.GetByCourierIdAsync(id, ct) is { } user)
                await _users.DeleteAsync(user.Id, ct);

            await _couriers.DeleteAsync(id, ct);
            return ServiceResult<bool>.Ok(true);
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    private static Dictionary<string, string[]> ValidateCourier(string? rawName, string? rawPhone, out string name, out string? phone)
    {
        var errors = new Dictionary<string, string[]>();
        name = rawName?.Trim() ?? "";
        phone = string.IsNullOrWhiteSpace(rawPhone) ? null : rawPhone.Trim();

        if (name.Length is < 2 or > 60)
            errors["Name"] = ["Ad 2-60 karakter olmalı."];

        if (phone is not null && !ShipmentService.IsValidPhone(phone))
            errors["Phone"] = ["Telefon numarası 7-20 karakter olmalı; yalnızca rakam, +, -, boşluk ve parantez içerebilir."];

        return errors;
    }

    /// <summary>Null when the vehicle is fine to use; otherwise the problem to return. A vehicle belongs to one courier at a time.</summary>
    private async Task<ServiceResult<T>?> CheckVehicleAsync<T>(Guid? vehicleId, Guid? exceptCourierId, CancellationToken ct)
    {
        if (vehicleId is null) return null;

        if (await _vehicles.GetAsync(vehicleId.Value, ct) is null)
            return ServiceResult<T>.Invalid(new() { ["VehicleId"] = ["Araç bulunamadı."] });

        if ((await _couriers.ListAsync(ct)).Any(c => c.VehicleId == vehicleId && c.Id != exceptCourierId))
            return ServiceResult<T>.Conflict("Bu araç başka bir kuryeye atanmış.");

        return null;
    }

    private async Task<IReadOnlyList<CourierDto>> BuildCourierDtosAsync(IEnumerable<Courier> couriers, CancellationToken ct)
    {
        var vehicles = (await _vehicles.ListAsync(ct)).ToDictionary(v => v.Id);
        var open = (await _shipments.ListAsync(null, null, null, ct))
            .Where(s => s.CourierId is not null && !s.IsClosed)
            .ToLookup(s => s.CourierId!.Value);

        var result = new List<CourierDto>();
        foreach (var courier in couriers)
        {
            var user = await _users.GetByCourierIdAsync(courier.Id, ct);
            var vehicle = courier.VehicleId is { } vid && vehicles.TryGetValue(vid, out var v) ? v : null;
            var mine = open[courier.Id].ToList();

            result.Add(new CourierDto(courier.Id, courier.Name, courier.Phone, courier.VehicleId,
                vehicle?.Plate, vehicle?.CapacityKg, user?.Username, mine.Count, mine.Sum(s => s.WeightKg)));
        }
        return result;
    }

    /// <summary>Passes a failed account creation on as a courier error (the form field is called "Name", not "DisplayName").</summary>
    private static ServiceResult<T> Relay<T>(ServiceResult<AppUser> failed) =>
        new(failed.Kind, default,
            failed.Errors?.ToDictionary(e => e.Key == "DisplayName" ? "Name" : e.Key, e => e.Value),
            failed.Message);

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
