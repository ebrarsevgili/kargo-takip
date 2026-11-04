namespace KargoTakip.Api.Models;

public class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Plate { get; set; } = "";      // normalized: upper case, no spaces (e.g. 15ABC123)
    public string Type { get; set; } = "";       // van, truck, motorbike ...
    public decimal CapacityKg { get; set; }
}

public class Courier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public Guid? VehicleId { get; set; }
}
