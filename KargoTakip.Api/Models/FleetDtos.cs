namespace KargoTakip.Api.Models;

// ---- Vehicles ----

public record VehicleRequest(string? Plate, string? Type, decimal? CapacityKg);

public record VehicleDto(Guid Id, string Plate, string Type, decimal CapacityKg, Guid? CourierId, string? CourierName);

// ---- Couriers ----

/// <summary>Creating a courier also creates the login the courier uses to update their own shipments.</summary>
public record CreateCourierRequest(string? Name, string? Phone, Guid? VehicleId, string? Username, string? Password);

public record UpdateCourierRequest(string? Name, string? Phone, Guid? VehicleId);

public record CourierDto(
    Guid Id,
    string Name,
    string? Phone,
    Guid? VehicleId,
    string? VehiclePlate,
    decimal? CapacityKg,
    string? Username,
    int ActiveShipments,
    decimal ActiveWeightKg);

// ---- Assignment ----

/// <summary>A null CourierId removes the current assignment.</summary>
public record AssignCourierRequest(Guid? CourierId);
