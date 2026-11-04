using KargoTakip.Api.Models;
using KargoTakip.Api.Storage;

namespace KargoTakip.Api.Services;

/// <summary>Turns stored shipments into the DTOs sent to the browser, resolving courier and owner names.</summary>
public sealed class ShipmentMapper
{
    private readonly ICourierRepository _couriers;
    private readonly IUserRepository _users;

    public ShipmentMapper(ICourierRepository couriers, IUserRepository users)
    {
        _couriers = couriers;
        _users = users;
    }

    public static IReadOnlyList<TrackingEventDto> ToEventDtos(Shipment s) =>
        s.Events.OrderBy(e => e.Timestamp)
                .Select(e => new TrackingEventDto(e.Status, e.Location, e.Note, e.Timestamp))
                .ToList();

    public static PublicTrackingDto ToPublic(Shipment s) => new(
        s.TrackingNumber, s.Status, s.OriginCity, s.DestinationCity,
        ShipmentService.MaskName(s.ReceiverName), s.CreatedAt, ToEventDtos(s));

    public async Task<ShipmentDto> ToDtoAsync(Shipment shipment, CancellationToken ct) =>
        (await ToDtosAsync([shipment], ct))[0];

    public async Task<IReadOnlyList<ShipmentDto>> ToDtosAsync(IEnumerable<Shipment> shipments, CancellationToken ct)
    {
        var list = shipments.ToList();
        var courierNames = (await _couriers.ListAsync(ct)).ToDictionary(c => c.Id, c => c.Name);

        var ownerNames = new Dictionary<Guid, string>();
        foreach (var ownerId in list.Where(s => s.OwnerUserId is not null).Select(s => s.OwnerUserId!.Value).Distinct())
            if (await _users.GetByIdAsync(ownerId, ct) is { } owner) ownerNames[ownerId] = owner.Username;

        return list.Select(s => new ShipmentDto(
            s.TrackingNumber, s.SenderName, s.ReceiverName, s.ReceiverPhone,
            s.OriginCity, s.DestinationCity, s.WeightKg, s.Status, s.CreatedAt,
            s.OwnerUserId is { } o && ownerNames.TryGetValue(o, out var ownerName) ? ownerName : null,
            s.CourierId,
            s.CourierId is { } c && courierNames.TryGetValue(c, out var courierName) ? courierName : null,
            ToEventDtos(s))).ToList();
    }
}
