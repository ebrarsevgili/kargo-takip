using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KargoTakip.Api.Models;
using KargoTakip.Api.Storage;

namespace KargoTakip.Api.Services;

public enum ResultKind { Ok, NotFound, Invalid, Conflict }

public record ServiceResult<T>(
    ResultKind Kind,
    T? Value = default,
    Dictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    public static ServiceResult<T> Ok(T value) => new(ResultKind.Ok, value);
    public static ServiceResult<T> NotFound() => new(ResultKind.NotFound);
    public static ServiceResult<T> Invalid(Dictionary<string, string[]> errors) => new(ResultKind.Invalid, Errors: errors);
    public static ServiceResult<T> Conflict(string message) => new(ResultKind.Conflict, Message: message);
}

/// <summary>Business rules live here: validation, tracking number generation, status transitions.</summary>
public sealed partial class ShipmentService
{
    private readonly IShipmentRepository _repo;
    private readonly TimeProvider _time;

    // Serializes writes so two admins adding events to the same shipment cannot overwrite each other.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ShipmentService(IShipmentRepository repo, TimeProvider time)
    {
        _repo = repo;
        _time = time;
    }

    [GeneratedRegex(@"^KT\d{10}$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackingFormat();

    [GeneratedRegex(@"^[0-9+\-\s()]{7,20}$")]
    private static partial Regex PhoneFormat();

    public static bool IsValidTrackingNumber(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TrackingFormat().IsMatch(value.Trim());

    public static string Normalize(string trackingNumber) => trackingNumber.Trim().ToUpperInvariant();

    public Task<Shipment?> GetAsync(string trackingNumber, CancellationToken ct) =>
        _repo.GetAsync(Normalize(trackingNumber), ct);

    public Task<IReadOnlyList<Shipment>> ListAsync(ShipmentStatus? status, CancellationToken ct) =>
        _repo.ListAsync(status, ct);

    public async Task<ServiceResult<Shipment>> CreateAsync(CreateShipmentRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var sender = Clean(req.SenderName);
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

    public async Task<ServiceResult<Shipment>> AddEventAsync(string trackingNumber, AddEventRequest req, CancellationToken ct)
    {
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
            var shipment = await _repo.GetAsync(Normalize(trackingNumber), ct);
            if (shipment is null) return ServiceResult<Shipment>.NotFound();

            if (shipment.IsClosed)
                return ServiceResult<Shipment>.Conflict(
                    "Teslim edilmiş veya iptal edilmiş bir kargoya yeni olay eklenemez.");

            var newStatus = req.Status!.Value;
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
        throw new InvalidOperationException("Benzersiz takip numarası üretilemedi.");
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
