namespace KargoTakip.Api.Models;

// ---- Requests ----

public record CreateShipmentRequest(
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

/// <summary>Admin view: all fields.</summary>
public record AdminShipmentDto(
    string TrackingNumber,
    string SenderName,
    string ReceiverName,
    string? ReceiverPhone,
    string OriginCity,
    string DestinationCity,
    decimal WeightKg,
    ShipmentStatus Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TrackingEventDto> Events);
