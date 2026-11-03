using System.Text.Json.Serialization;
using KargoTakip.Api.Models;
using KargoTakip.Api.Services;
using KargoTakip.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

// Send and receive enums as readable names (e.g. "InTransit") instead of numbers.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();

// Malformed JSON / invalid enum values should return 400 in every environment
// (by default Development throws and the exception handler turns it into a 500).
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);

// ---- Storage and services ----
var dataFile = builder.Configuration["Storage:FilePath"] ?? "data/shipments.json";
if (!Path.IsPathRooted(dataFile))
    dataFile = Path.Combine(builder.Environment.ContentRootPath, dataFile);

builder.Services.AddSingleton(new StorageOptions(dataFile));
builder.Services.AddSingleton<IShipmentRepository, JsonShipmentRepository>(); // Switching storage later only changes this line.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ShipmentService>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => "Cargo tracking API is running.");

// ===================================================================
// Public: look up a shipment by tracking number
// ===================================================================
app.MapGet("/api/track/{trackingNumber}", async (string trackingNumber, ShipmentService service, CancellationToken ct) =>
{
    if (!ShipmentService.IsValidTrackingNumber(trackingNumber))
        return Results.Problem(
            title: "Geçersiz takip numarası",
            detail: "Takip numarası KT ile başlar ve ardından 10 rakam gelir (örn. KT1234567890).",
            statusCode: StatusCodes.Status400BadRequest);

    var shipment = await service.GetAsync(trackingNumber, ct);
    return shipment is null
        ? Results.Problem(title: "Kargo bulunamadı", statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(ToPublic(shipment));
});

app.Run();

// ===================================================================
// Helpers
// ===================================================================

static IReadOnlyList<TrackingEventDto> ToEventDtos(Shipment s) =>
    s.Events.OrderBy(e => e.Timestamp)
            .Select(e => new TrackingEventDto(e.Status, e.Location, e.Note, e.Timestamp))
            .ToList();

static PublicTrackingDto ToPublic(Shipment s) => new(
    s.TrackingNumber, s.Status, s.OriginCity, s.DestinationCity,
    ShipmentService.MaskName(s.ReceiverName), s.CreatedAt, ToEventDtos(s));
