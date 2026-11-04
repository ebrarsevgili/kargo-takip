using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
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

// ---- Rate limit the public tracking endpoint against tracking-number guessing (30 requests per minute per IP) ----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-tracking", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();

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
})
.RequireRateLimiting("public-tracking");

// ===================================================================
// Admin endpoints: require the X-Api-Key header
// ===================================================================
var admin = app.MapGroup("/api/shipments");

admin.AddEndpointFilter(async (context, next) =>
{
    var configured = context.HttpContext.RequestServices
        .GetRequiredService<IConfiguration>()["Admin:ApiKey"];

    if (string.IsNullOrWhiteSpace(configured))
        return Results.Problem(
            title: "Yönetici anahtarı tanımlı değil",
            detail: "Admin:ApiKey ayarını yapılandırmadan yönetici uçları kapalıdır.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    var provided = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
    if (!SecretsMatch(provided, configured))
        return Results.Problem(
            title: "Yetkisiz",
            detail: "Geçerli bir X-Api-Key başlığı gönder.",
            statusCode: StatusCodes.Status401Unauthorized);

    return await next(context);
});

admin.MapPost("/", async (CreateShipmentRequest request, ShipmentService service, CancellationToken ct) =>
{
    var result = await service.CreateAsync(request, ct);
    return result.Kind == ResultKind.Ok
        ? Results.Created($"/api/shipments/{result.Value!.TrackingNumber}", ToAdmin(result.Value))
        : ToProblem(result);
});

admin.MapGet("/", async (ShipmentStatus? status, ShipmentService service, CancellationToken ct) =>
{
    var items = await service.ListAsync(status, ct);
    return Results.Ok(items.Select(ToAdmin));
});

admin.MapGet("/{trackingNumber}", async (string trackingNumber, ShipmentService service, CancellationToken ct) =>
{
    var shipment = await service.GetAsync(trackingNumber, ct);
    return shipment is null
        ? Results.Problem(title: "Kargo bulunamadı", statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(ToAdmin(shipment));
});

admin.MapPost("/{trackingNumber}/events", async (string trackingNumber, AddEventRequest request, ShipmentService service, CancellationToken ct) =>
{
    var result = await service.AddEventAsync(trackingNumber, request, ct);
    return result.Kind == ResultKind.Ok ? Results.Ok(ToAdmin(result.Value!)) : ToProblem(result);
});

app.Run();

// ===================================================================
// Helpers
// ===================================================================

static IResult ToProblem<T>(ServiceResult<T> result) => result.Kind switch
{
    ResultKind.Invalid => Results.ValidationProblem(result.Errors!),
    ResultKind.NotFound => Results.Problem(title: "Kargo bulunamadı", statusCode: StatusCodes.Status404NotFound),
    ResultKind.Conflict => Results.Problem(title: "İşlem yapılamadı", detail: result.Message, statusCode: StatusCodes.Status409Conflict),
    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
};

// Compare SHA-256 digests in constant time so neither timing nor length leaks information about the key.
static bool SecretsMatch(string provided, string expected) =>
    CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
        SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

static IReadOnlyList<TrackingEventDto> ToEventDtos(Shipment s) =>
    s.Events.OrderBy(e => e.Timestamp)
            .Select(e => new TrackingEventDto(e.Status, e.Location, e.Note, e.Timestamp))
            .ToList();

static PublicTrackingDto ToPublic(Shipment s) => new(
    s.TrackingNumber, s.Status, s.OriginCity, s.DestinationCity,
    ShipmentService.MaskName(s.ReceiverName), s.CreatedAt, ToEventDtos(s));

static AdminShipmentDto ToAdmin(Shipment s) => new(
    s.TrackingNumber, s.SenderName, s.ReceiverName, s.ReceiverPhone,
    s.OriginCity, s.DestinationCity, s.WeightKg, s.Status, s.CreatedAt, ToEventDtos(s));
