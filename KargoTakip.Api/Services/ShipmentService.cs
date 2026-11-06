using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KargoTakip.Api.Models;
using KargoTakip.Api.Security;
using KargoTakip.Api.Storage;

namespace KargoTakip.Api.Services;

/// <summary>
/// Business rules live here: validation, tracking number generation, status transitions and who may do what.
/// Admins can do everything, a courier only works with the shipments assigned to them,
/// a customer only creates and sees their own shipments.
/// </summary>
public sealed partial class ShipmentService
{
    private readonly IShipmentRepository _repo;
    private readonly IUserRepository _users;
    private readonly ICourierRepository _couriers;
    private readonly IVehicleRepository _vehicles;
    private readonly TimeProvider _time;

    public ShipmentService(IShipmentRepository repo, IUserRepository users, ICourierRepository couriers,
        IVehicleRepository vehicles, TimeProvider time)
    {
        _repo = repo;
        _users = users;
        _couriers = couriers;
        _vehicles = vehicles;
        _time = time;
    }

    [GeneratedRegex(@"^KT\d{10}$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackingFormat();

    [GeneratedRegex(@"^[0-9+\-\s()]{7,20}$")]
    private static partial Regex PhoneFormat();

    public static bool IsValidTrackingNumber(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TrackingFormat().IsMatch(value.Trim());

    public static bool IsValidPhone(string value) => PhoneFormat().IsMatch(value);

    public static string Normalize(string trackingNumber) => trackingNumber.Trim().ToUpperInvariant();

    // ---- Reading ----

    /// <summary>Public lookup: anyone who knows the tracking number may see the limited public view.</summary>
    public Task<Shipment?> GetAsync(string trackingNumber, CancellationToken ct) =>
        _repo.GetAsync(Normalize(trackingNumber), ct);

    /// <summary>
    /// Returns the shipment only if this user may see it, otherwise null. "Not allowed" looks the same as
    /// "does not exist", so nobody can probe which tracking numbers exist.
    /// </summary>
    public async Task<Shipment?> GetForAsync(Actor actor, string trackingNumber, CancellationToken ct)
    {
        var shipment = await _repo.GetAsync(Normalize(trackingNumber), ct);
        return shipment is not null && CanSee(actor, shipment) ? shipment : null;
    }

    public Task<IReadOnlyList<Shipment>> ListAsync(Actor actor, ShipmentStatus? status, CancellationToken ct) =>
        actor.Role switch
        {
            UserRole.Admin => _repo.ListAsync(status, null, null, ct),
            UserRole.Courier => actor.CourierId is { } courierId
                ? _repo.ListAsync(status, null, courierId, ct)
                : Task.FromResult<IReadOnlyList<Shipment>>([]),
            _ => _repo.ListAsync(status, actor.UserId, null, ct)
        };

    public async Task<StatsDto> StatsAsync(Actor actor, CancellationToken ct)
    {
        var visible = await ListAsync(actor, null, ct);

        return new StatsDto(
            visible.Count,
            visible.Count(s => !s.IsClosed),
            Enum.GetValues<ShipmentStatus>().ToDictionary(status => status.ToString(), status => visible.Count(s => s.Status == status)),
            actor.Role == UserRole.Admin ? visible.Count(s => !s.IsClosed && s.CourierId is null) : null);
    }

    public const int MaxPageSize = 50;

    /// <summary>Filtered, newest-first, paged list of the shipments this user may see.</summary>
    public async Task<PagedResult<Shipment>> SearchAsync(Actor actor, ShipmentFilter filter, CancellationToken ct)
    {
        var visible = await ListAsync(actor, filter.Status, ct);

        if (filter.Unassigned) visible = visible.Where(s => !s.IsClosed && s.CourierId is null).ToList();

        var term = filter.Search?.Trim();
        IEnumerable<Shipment> matches = string.IsNullOrEmpty(term)
            ? visible
            : visible.Where(s => TextSearch.MatchesAll(term,
                s.TrackingNumber, s.SenderName, s.ReceiverName, s.ReceiverPhone, s.OriginCity, s.DestinationCity));

        var ordered = matches.OrderByDescending(s => s.CreatedAt).ToList();

        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
        var totalPages = Math.Max(1, (int)Math.Ceiling(ordered.Count / (double)pageSize));
        var page = Math.Clamp(filter.Page, 1, totalPages);

        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<Shipment>(items, ordered.Count, page, pageSize, totalPages);
    }

    private static bool CanSee(Actor actor, Shipment shipment) => actor.Role switch
    {
        UserRole.Admin => true,
        UserRole.Courier => shipment.CourierId is not null && shipment.CourierId == actor.CourierId,
        UserRole.Customer => shipment.OwnerUserId == actor.UserId,
        _ => false
    };

    // ---- Creating ----

    public async Task<ServiceResult<Shipment>> CreateAsync(Actor actor, CreateShipmentRequest req, CancellationToken ct)
    {
        if (actor.Role == UserRole.Courier)
            return ServiceResult<Shipment>.Forbidden("Kuryeler kargo oluşturamaz.");

        var errors = new Dictionary<string, string[]>();

        // A customer's shipments are always their own; the sender defaults to their display name.
        var sender = Clean(req.SenderName) ?? (actor.Role == UserRole.Customer ? actor.DisplayName : null);
        var receiver = Clean(req.ReceiverName);
        var phone = Clean(req.ReceiverPhone);
        var origin = Clean(req.OriginCity);
        var destination = Clean(req.DestinationCity);

        ValidateCore(errors, sender, receiver, phone, origin, destination, req.WeightKg);

        Guid? ownerId = actor.Role == UserRole.Customer ? actor.UserId : null;
        if (actor.Role == UserRole.Admin && Clean(req.OwnerUsername) is { } ownerUsername)
        {
            var owner = await _users.GetByUsernameAsync(ownerUsername, ct);
            if (owner is null || owner.Role != UserRole.Customer)
                errors[nameof(req.OwnerUsername)] = new[] { "Bu kullanıcı adıyla kayıtlı bir müşteri yok." };
            else
                ownerId = owner.Id;
        }

        if (errors.Count > 0) return ServiceResult<Shipment>.Invalid(errors);

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            var now = _time.GetUtcNow();
            var shipment = new Shipment
            {
                TrackingNumber = await NewTrackingNumberAsync(ct),
                SenderName = sender!,
                ReceiverName = receiver!,
                ReceiverPhone = phone,
                OriginCity = origin!,
                DestinationCity = destination!,
                WeightKg = Math.Round(req.WeightKg!.Value, 2),
                OwnerUserId = ownerId,
                Status = ShipmentStatus.Created,
                CreatedAt = now,
                Events =
                {
                    new TrackingEvent
                    {
                        Status = ShipmentStatus.Created,
                        Location = origin!,
                        Note = "Kargo kaydı oluşturuldu.",
                        Timestamp = now
                    }
                }
            };

            await _repo.AddAsync(shipment, ct);
            return ServiceResult<Shipment>.Ok(shipment);
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    // ---- Editing and deleting (admin only) ----

    public async Task<ServiceResult<Shipment>> UpdateAsync(Actor actor, string trackingNumber, UpdateShipmentRequest req, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin)
            return ServiceResult<Shipment>.Forbidden("Kargo bilgilerini yalnızca yöneticiler düzenleyebilir.");

        var errors = new Dictionary<string, string[]>();
        var sender = Clean(req.SenderName);
        var receiver = Clean(req.ReceiverName);
        var phone = Clean(req.ReceiverPhone);
        var origin = Clean(req.OriginCity);
        var destination = Clean(req.DestinationCity);

        ValidateCore(errors, sender, receiver, phone, origin, destination, req.WeightKg);
        if (errors.Count > 0) return ServiceResult<Shipment>.Invalid(errors);

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            var shipment = await _repo.GetAsync(Normalize(trackingNumber), ct);
            if (shipment is null) return ServiceResult<Shipment>.NotFound();

            if (shipment.IsClosed)
                return ServiceResult<Shipment>.Conflict("Teslim edilmiş veya iptal edilmiş bir kargo düzenlenemez.");

            shipment.SenderName = sender!;
            shipment.ReceiverName = receiver!;
            shipment.ReceiverPhone = phone;
            shipment.OriginCity = origin!;
            shipment.DestinationCity = destination!;
            shipment.WeightKg = Math.Round(req.WeightKg!.Value, 2);

            await _repo.UpdateAsync(shipment, ct);
            return ServiceResult<Shipment>.Ok(shipment);
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Actor actor, string trackingNumber, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin)
            return ServiceResult<bool>.Forbidden("Kargo silme yetkisi yalnızca yöneticilerde.");

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            return await _repo.DeleteAsync(Normalize(trackingNumber), ct)
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.NotFound();
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    // ---- Courier assignment (admin only) ----

    /// <summary>
    /// Hands an open shipment to a courier (or takes it back when <paramref name="courierId"/> is null).
    /// The courier's vehicle must be able to carry the total weight of all their open shipments.
    /// </summary>
    public async Task<ServiceResult<Shipment>> AssignCourierAsync(Actor actor, string trackingNumber, Guid? courierId, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin)
            return ServiceResult<Shipment>.Forbidden("Kurye atamasını yalnızca yöneticiler yapabilir.");

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            var shipment = await _repo.GetAsync(Normalize(trackingNumber), ct);
            if (shipment is null) return ServiceResult<Shipment>.NotFound();

            if (shipment.IsClosed)
                return ServiceResult<Shipment>.Conflict("Teslim edilmiş veya iptal edilmiş bir kargoya kurye atanamaz.");

            if (courierId is { } id)
            {
                var courier = await _couriers.GetAsync(id, ct);
                if (courier is null)
                    return ServiceResult<Shipment>.Invalid(new() { [nameof(AssignCourierRequest.CourierId)] = new[] { "Kurye bulunamadı." } });

                if (courier.VehicleId is { } vehicleId && await _vehicles.GetAsync(vehicleId, ct) is { } vehicle)
                {
                    var load = (await _repo.ListAsync(null, null, id, ct))
                        .Where(s => !s.IsClosed && s.TrackingNumber != shipment.TrackingNumber)
                        .Sum(s => s.WeightKg);

                    if (load + shipment.WeightKg > vehicle.CapacityKg)
                        return ServiceResult<Shipment>.Conflict(
                            $"Araç kapasitesi aşılır: {vehicle.Plate} en fazla {vehicle.CapacityKg:0.##} kg taşır, " +
                            $"kuryenin mevcut yükü {load:0.##} kg, bu kargo {shipment.WeightKg:0.##} kg.");
                }
            }

            shipment.CourierId = courierId;
            await _repo.UpdateAsync(shipment, ct);
            return ServiceResult<Shipment>.Ok(shipment);
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    // ---- Movements ----

    public async Task<ServiceResult<Shipment>> AddEventAsync(Actor actor, string trackingNumber, AddEventRequest req, CancellationToken ct)
    {
        if (actor.Role == UserRole.Customer)
            return ServiceResult<Shipment>.Forbidden("Müşteriler hareket ekleyemez.");

        var errors = new Dictionary<string, string[]>();
        var location = Clean(req.Location);
        var note = Clean(req.Note);

        if (req.Status is null || !Enum.IsDefined(req.Status.Value))
            errors[nameof(req.Status)] = new[] { "Geçerli bir durum seçmelisin." };
        else if (req.Status == ShipmentStatus.Created)
            errors[nameof(req.Status)] = new[] { "'Created' durumu yalnızca kargo oluşturulurken otomatik eklenir." };

        Require(errors, nameof(req.Location), location, 100, "Konum");

        if (note is { Length: > 300 })
            errors[nameof(req.Note)] = new[] { "Not en fazla 300 karakter olabilir." };

        if (errors.Count > 0) return ServiceResult<Shipment>.Invalid(errors);

        await DataLock.Gate.WaitAsync(ct);
        try
        {
            var shipment = await GetForAsync(actor, trackingNumber, ct);
            if (shipment is null) return ServiceResult<Shipment>.NotFound();

            var newStatus = req.Status!.Value;
            if (actor.Role == UserRole.Courier && newStatus == ShipmentStatus.Cancelled)
                return ServiceResult<Shipment>.Forbidden("Kuryeler kargoyu iptal edemez.");

            if (shipment.IsClosed)
                return ServiceResult<Shipment>.Conflict(
                    "Teslim edilmiş veya iptal edilmiş bir kargoya yeni olay eklenemez.");

            if (newStatus != ShipmentStatus.Cancelled && Rank(newStatus) < Rank(shipment.Status))
                return ServiceResult<Shipment>.Invalid(new()
                {
                    [nameof(req.Status)] = new[]
                    {
                        $"Kargo şu an '{shipment.Status}' durumunda; '{newStatus}' durumuna geri dönülemez."
                    }
                });

            shipment.Events.Add(new TrackingEvent
            {
                Status = newStatus,
                Location = location!,
                Note = note,
                Timestamp = _time.GetUtcNow()
            });
            shipment.Status = newStatus;

            await _repo.UpdateAsync(shipment, ct);
            return ServiceResult<Shipment>.Ok(shipment);
        }
        finally
        {
            DataLock.Gate.Release();
        }
    }

    // ---- Helpers ----

    /// <summary>"Jane Doe" → "J*** D**". Hides the receiver's identity on the public page.</summary>
    public static string MaskName(string name) =>
        string.Join(' ', name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length <= 1 ? w : w[0] + new string('*', w.Length - 1)));

    // Normal flow: Created → PickedUp → InTransit → OutForDelivery → Delivered. Cancelling is possible any time until the shipment is closed.
    private static int Rank(ShipmentStatus s) => s switch
    {
        ShipmentStatus.Created => 0,
        ShipmentStatus.PickedUp => 1,
        ShipmentStatus.InTransit => 2,
        ShipmentStatus.OutForDelivery => 3,
        ShipmentStatus.Delivered => 4,
        _ => int.MaxValue
    };

    private async Task<string> NewTrackingNumberAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate =
                $"KT{RandomNumberGenerator.GetInt32(0, 100_000):D5}{RandomNumberGenerator.GetInt32(0, 100_000):D5}";
            if (!await _repo.ExistsAsync(candidate, ct)) return candidate;
        }
        throw new InvalidOperationException("Could not generate a unique tracking number.");
    }

    // Field rules shared by "create" and "edit".
    private static void ValidateCore(Dictionary<string, string[]> errors,
        string? sender, string? receiver, string? phone, string? origin, string? destination, decimal? weightKg)
    {
        Require(errors, nameof(CreateShipmentRequest.SenderName), sender, 100, "Gönderen adı");
        Require(errors, nameof(CreateShipmentRequest.ReceiverName), receiver, 100, "Alıcı adı");
        Require(errors, nameof(CreateShipmentRequest.OriginCity), origin, 80, "Çıkış şehri");
        Require(errors, nameof(CreateShipmentRequest.DestinationCity), destination, 80, "Varış şehri");

        if (phone is not null && !PhoneFormat().IsMatch(phone))
            errors[nameof(CreateShipmentRequest.ReceiverPhone)] =
                new[] { "Telefon numarası 7-20 karakter olmalı; yalnızca rakam, +, -, boşluk ve parantez içerebilir." };

        if (weightKg is null or <= 0 or > 1000)
            errors[nameof(CreateShipmentRequest.WeightKg)] = new[] { "Ağırlık 0'dan büyük ve en fazla 1000 kg olmalı." };
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static void Require(Dictionary<string, string[]> errors, string field, string? value, int max, string label)
    {
        if (value is null)
            errors[field] = new[] { $"{label} zorunlu." };
        else if (value.Length > max)
            errors[field] = new[] { $"{label} en fazla {max} karakter olabilir." };
    }
}
