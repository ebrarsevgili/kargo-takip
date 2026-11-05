using KargoTakip.Api.Models;
using KargoTakip.Api.Security;
using KargoTakip.Api.Services;

namespace KargoTakip.Api.Endpoints;

/// <summary>Vehicles and couriers. The route group requires the Admin role; FleetService checks it again.</summary>
public static class FleetEndpoints
{
    public static IEndpointRouteBuilder MapFleetEndpoints(this IEndpointRouteBuilder app)
    {
        MapVehicles(app.MapGroup("/api/vehicles").RequireAuthorization("AdminOnly"));
        return app;
    }

    private static void MapVehicles(RouteGroupBuilder group)
    {
        group.MapGet("/", async (HttpContext http, FleetService fleet, CancellationToken ct) =>
        {
            if (Actor.From(http.User) is not { } actor) return Results.Unauthorized();

            var result = await fleet.ListVehiclesAsync(actor, ct);
            return result.Kind == ResultKind.Ok ? Results.Ok(result.Value) : result.ToProblem();
        });

        group.MapPost("/", async (HttpContext http, VehicleRequest request, FleetService fleet, CancellationToken ct) =>
        {
            if (Actor.From(http.User) is not { } actor) return Results.Unauthorized();

            var result = await fleet.CreateVehicleAsync(actor, request, ct);
            return result.Kind == ResultKind.Ok
                ? Results.Created($"/api/vehicles/{result.Value!.Id}", result.Value)
                : result.ToProblem();
        });

        group.MapPut("/{id:guid}", async (HttpContext http, Guid id, VehicleRequest request, FleetService fleet, CancellationToken ct) =>
        {
            if (Actor.From(http.User) is not { } actor) return Results.Unauthorized();

            var result = await fleet.UpdateVehicleAsync(actor, id, request, ct);
            return result.Kind == ResultKind.Ok ? Results.Ok(result.Value) : result.ToProblem();
        });

        group.MapDelete("/{id:guid}", async (HttpContext http, Guid id, FleetService fleet, CancellationToken ct) =>
        {
            if (Actor.From(http.User) is not { } actor) return Results.Unauthorized();

            var result = await fleet.DeleteVehicleAsync(actor, id, ct);
            return result.Kind == ResultKind.Ok ? Results.NoContent() : result.ToProblem();
        });
    }
}
