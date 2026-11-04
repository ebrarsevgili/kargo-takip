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
    private readonly TimeProvider _time;

    // Serializes writes so two people adding events to the same shipment cannot overwrite each other.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ShipmentService(IShipmentRepository repo, IUserRepository users, TimeProvider time)
    {
        _repo = repo;
        _users = users;
        _time = time;
    }

    [GeneratedRegex(@"^KT\d{10}$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackingFormat();

    [GeneratedRegex(@"^[0-9+\-\s()]{7,20}$")]
    private static partial Regex PhoneFormat();

    public static bool IsValidTrackingNumber(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TrackingFormat().IsMatch(value.Trim());

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

        Require(errors, nameof(req.SenderName), sender, 100, "Gönderen adı");
        Require(errors, nameof(req.ReceiverName), receiver, 100, "Alıcı adı");
        Require(errors, nameof(req.OriginCity), origin, 80, "Çıkış şehri");
        Require(errors, nameof(req.DestinationCity), destination, 80, "Varış şehri");

        if (phone is not null && !PhoneFormat().IsMatch(phone))
            errors[nameof(req.ReceiverPhone)] = new[] { "Telefon numarası 7-20 karakter olmalı; yalnızca rakam, +, -, boşluk ve parantez içerebilir." };

        if (req.WeightKg is null or <= 0 or > 1000)
            errors[nameof(req.WeightKg)] = new[] { "Ağırlık 0'dan büyük ve en fazla 1000 kg olmalı." };

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

        await _writeLock.WaitAsync(ct);
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
            _writeLock.Release();
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

        await _writeLock.WaitAsync(ct);
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
            _writeLock.Release();
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
