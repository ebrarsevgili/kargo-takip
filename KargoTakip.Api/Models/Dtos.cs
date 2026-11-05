namespace KargoTakip.Api.Models;

// ---- Requests ----

public record CreateShipmentRequest(
    string? SenderName,
    string? ReceiverName,
    string? ReceiverPhone,
    string? OriginCity,
    string? DestinationCity,
    decimal? WeightKg,
    string? OwnerUsername = null);   // admins may create a shipment on behalf of a customer

public record UpdateShipmentRequest(
    string? SenderName,
    string? ReceiverName,
    string? ReceiverPhone,
    string? OriginCity,
    string? DestinationCity,
    decimal? WeightKg);

public record AddEventRequest(
    ShipmentStatus? Status,
    string? Location,
    string? Note);

// ---- Responses ----

public record TrackingEventDto(ShipmentStatus Status, string Location, string? Note, DateTimeOffset Timestamp);

/// <summary>Limited view shown on the public tracking page (receiver name masked, no phone).</summary>
public record PublicTrackingDto(
    string TrackingNumber,
    ShipmentStatus Status,
    string OriginCity,
    string DestinationCity,
    string ReceiverMasked,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TrackingEventDto> Events);

/// <summary>Full view for signed-in users (access is limited by role, see ShipmentService).</summary>
public record ShipmentDto(
    string TrackingNumber,
    string SenderName,
    string ReceiverName,
    string? ReceiverPhone,
    string OriginCity,
    string DestinationCity,
    decimal WeightKg,
    ShipmentStatus Status,
    DateTimeOffset CreatedAt,
    string? OwnerUsername,
    Guid? CourierId,
    string? CourierName,
    IReadOnlyList<TrackingEventDto> Events);

/// <summary>Numbers for the dashboard header, counted over the shipments the signed-in user may see.</summary>
public record StatsDto(
    int Total,
    int Open,
    IReadOnlyDictionary<string, int> ByStatus,
    int? Unassigned);   // admins only: open shipments that have no courier yet
