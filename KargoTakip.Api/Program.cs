using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using KargoTakip.Api.Endpoints;
using KargoTakip.Api.Models;
using KargoTakip.Api.Services;
using KargoTakip.Api.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Send and receive enums as readable names (e.g. "InTransit") instead of numbers.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();

// Malformed JSON / invalid enum values should return 400 in every environment
// (by default Development throws and the exception handler turns it into a 500).
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);

// ---- Storage and services ----
var dataDirectory = builder.Configuration["Storage:DataDirectory"] ?? "data";
if (!Path.IsPathRooted(dataDirectory))
    dataDirectory = Path.Combine(builder.Environment.ContentRootPath, dataDirectory);

builder.Services.AddSingleton(new StorageOptions(dataDirectory));
// Switching storage later only changes these registrations.
builder.Services.AddSingleton<IShipmentRepository, JsonShipmentRepository>();
builder.Services.AddSingleton<IUserRepository, JsonUserRepository>();
builder.Services.AddSingleton<ICourierRepository, JsonCourierRepository>();
builder.Services.AddSingleton<IVehicleRepository, JsonVehicleRepository>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ShipmentService>();
builder.Services.AddSingleton<AuthService>();

// ---- Authentication: an encrypted, signed cookie issued at sign-in ----
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "kargo.auth";
        options.Cookie.HttpOnly = true;                                  // not readable from JavaScript
        options.Cookie.SameSite = SameSiteMode.Strict;                   // never sent on cross-site requests (CSRF protection)
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;  // use Always when you serve over HTTPS
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // This is an API: answer 401 / 403 instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole(nameof(UserRole.Admin)));

// ---- Rate limits (per IP): tracking numbers cannot be guessed quickly, passwords cannot be brute-forced ----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static RateLimitPartition<string> PerIp(HttpContext http, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    options.AddPolicy("public-tracking", http => PerIp(http, 30));
    options.AddPolicy("auth", http => PerIp(http, 20));
});

var app = builder.Build();

// First start: create the admin account from configuration (Admin:Username / Admin:Password).
await app.Services.GetRequiredService<AuthService>()
    .SeedAdminAsync(app.Configuration["Admin:Username"], app.Configuration["Admin:Password"], app.Logger);

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseDefaultFiles();   // serves / from wwwroot/index.html
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();

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
// Admin endpoints (this group is rewritten with role-based access in the next steps)
// ===================================================================
var admin = app.MapGroup("/api/shipments").RequireAuthorization("AdminOnly");

admin.MapPost("/", async (CreateShipmentRequest request, ShipmentService service, CancellationToken ct) =>
{
    var result = await service.CreateAsync(request, ct);
    return result.Kind == ResultKind.Ok
        ? Results.Created($"/api/shipments/{result.Value!.TrackingNumber}", ToAdmin(result.Value))
        : result.ToProblem();
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
    return result.Kind == ResultKind.Ok ? Results.Ok(ToAdmin(result.Value!)) : result.ToProblem();
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

static AdminShipmentDto ToAdmin(Shipment s) => new(
    s.TrackingNumber, s.SenderName, s.ReceiverName, s.ReceiverPhone,
    s.OriginCity, s.DestinationCity, s.WeightKg, s.Status, s.CreatedAt, ToEventDtos(s));
