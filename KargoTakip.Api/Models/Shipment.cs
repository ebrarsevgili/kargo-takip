namespace KargoTakip.Api.Models;

public enum ShipmentStatus
{
    Created,          // Shipment record created
    PickedUp,         // Picked up from the sender
    InTransit,        // In transit between hubs
    OutForDelivery,   // Out for delivery
    Delivered,        // Delivered
    Cancelled         // Cancelled
}

public class TrackingEvent
{
    public ShipmentStatus Status { get; set; }
    public string Location { get; set; } = "";
    public string? Note { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public class Shipment
{
    public string TrackingNumber { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string ReceiverName { get; set; } = "";
    public string? ReceiverPhone { get; set; }
    public string OriginCity { get; set; } = "";
    public string DestinationCity { get; set; } = "";
    public decimal WeightKg { get; set; }
    public Guid? OwnerUserId { get; set; }   // customer account that owns the shipment (optional)
    public Guid? CourierId { get; set; }     // courier responsible for delivery (optional)
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Created;
    public DateTimeOffset CreatedAt { get; set; }
    public List<TrackingEvent> Events { get; set; } = new();

    public bool IsClosed => Status is ShipmentStatus.Delivered or ShipmentStatus.Cancelled;
}
