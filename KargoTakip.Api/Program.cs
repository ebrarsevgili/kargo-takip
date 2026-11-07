using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using KargoTakip.Api.Endpoints;
using KargoTakip.Api.Models;
using KargoTakip.Api.Security;
using KargoTakip.Api.Services;
using KargoTakip.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

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

// Storage:Provider is "Sqlite" (default) or "Json". Only these registrations know which one is used;
// the services talk to the repository interfaces.
var useSqlite = !string.Equals(builder.Configuration["Storage:Provider"], "Json", StringComparison.OrdinalIgnoreCase);
if (useSqlite)
{
    Directory.CreateDirectory(dataDirectory);
    var connectionString = $"Data Source={Path.Combine(dataDirectory, "kargo.db")}";
    builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));
    builder.Services.AddSingleton<IShipmentRepository, EfShipmentRepository>();
    builder.Services.AddSingleton<IUserRepository, EfUserRepository>();
    builder.Services.AddSingleton<ICourierRepository, EfCourierRepository>();
    builder.Services.AddSingleton<IVehicleRepository, EfVehicleRepository>();
}
else
{
    builder.Services.AddSingleton<IShipmentRepository, JsonShipmentRepository>();
    builder.Services.AddSingleton<IUserRepository, JsonUserRepository>();
    builder.Services.AddSingleton<ICourierRepository, JsonCourierRepository>();
    builder.Services.AddSingleton<IVehicleRepository, JsonVehicleRepository>();
}
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

        // On every request: is the account still there with the same role? Otherwise the cookie is rejected,
        // so deleting a courier takes effect immediately instead of when the cookie expires.
        options.Events.OnValidatePrincipal = async ctx =>
        {
            var actor = Actor.From(ctx.Principal!);
            var users = ctx.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
            var user = actor is null ? null : await users.GetByIdAsync(actor.UserId, ctx.HttpContext.RequestAborted);

            if (user is null || user.Role != actor!.Role || user.CourierId != actor.CourierId)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync();
            }
        };

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

// Create the database (or apply new migrations) before anything reads from it.
if (useSqlite)
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

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
