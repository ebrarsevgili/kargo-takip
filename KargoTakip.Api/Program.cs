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
builder.Services.AddSingleton<ShipmentMapper>();
builder.Services.AddSingleton<ShipmentService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<FleetService>();

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
        : Results.Ok(ShipmentMapper.ToPublic(shipment));
})
.RequireRateLimiting("public-tracking");

// ===================================================================
// Signed-in users: create, list, view and update shipments (rules are enforced per role in ShipmentService)
// ===================================================================
app.MapShipmentEndpoints();
app.MapFleetEndpoints();

app.Run();
