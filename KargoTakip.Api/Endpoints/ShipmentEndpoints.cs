using KargoTakip.Api.Models;
using KargoTakip.Api.Security;
using KargoTakip.Api.Services;

namespace KargoTakip.Api.Endpoints;

/// <summary>
/// Shipment API for signed-in users. Any role may call these endpoints; ShipmentService decides
/// what each role is allowed to see and do, and answers 403 / 404 accordingly.
/// </summary>
public static class ShipmentEndpoints
{
    public static IEndpointRouteBuilder MapShipmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shipments").RequireAuthorization();

        group.MapPost("/", async (HttpContext http, CreateShipmentRequest request,
            ShipmentService service, ShipmentMapper mapper, CancellationToken ct) =>
        {
            if (Actor.From(http.User) is not { } actor) return Results.Unauthorized();

            var result = await service.CreateAsync(actor, request, ct);
            return result.Kind == ResultKind.Ok
                ? Results.Created($"/api/shipments/{result.Value!.TrackingNumber}", await mapper.ToDtoAsync(result.Value, ct))
                : result.ToProblem();
        });

        // GET /api/shipments?status=InTransit&q=ankara&page=2&pageSize=10
        group.MapGet("/", async (HttpContext http, ShipmentStatus? status, string? q, int? page, int? pageSize,
            ShipmentService service, ShipmentMapper mapper, CancellationToken ct) =>
        {
            if (Actor.From(http.User) is not { } actor) return Results.Unauthorized();

            var result = await service.SearchAsync(actor, new ShipmentFilter(status, q, page ?? 1, pageSize ?? 10), ct);
            return Results.Ok(result.WithItems(await mapper.ToDtosAsync(result.Items, ct)));
        });

        group.MapGet("/{trackingNumber}", async (HttpContext http, string trackingNumber,
            ShipmentService service, ShipmentMapper mapper, CancellationToken ct) =>
        {
            if (Actor.From(http.User) is not { } actor) return Results.Unauthorized();

            var shipment = await service.GetForAsync(actor, trackingNumber, ct);
            return shipment is null
                ? Results.Problem(title: "Kargo bulunamadı", statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(await mapper.ToDtoAsync(shipment, ct));
        });

        group.MapPost("/{trackingNumber}/events", async (HttpContext http, string trackingNumber, AddEventRequest request,
            ShipmentService service, ShipmentMapper mapper, CancellationToken ct) =>
        {
            if (Actor.From(http.User) is not { } actor) return Results.Unauthorized();

            var result = await service.AddEventAsync(actor, trackingNumber, request, ct);
            return result.Kind == ResultKind.Ok
                ? Results.Ok(await mapper.ToDtoAsync(result.Value!, ct))
                : result.ToProblem();
        });

        // Editing and deleting are admin-only: the route policy is the first line of defence, the service checks again.
        group.MapPut("/{trackingNumber}", async (HttpContext http, string trackingNumber, UpdateShipmentRequest request,
            ShipmentService service, ShipmentMapper mapper, CancellationToken ct) =>
        {
            if (Actor.From(http.User) is not { } actor) return Results.Unauthorized();

            var result = await service.UpdateAsync(actor, trackingNumber, request, ct);
            return result.Kind == ResultKind.Ok
                ? Results.Ok(await mapper.ToDtoAsync(result.Value!, ct))
                : result.ToProblem();
        })
        .RequireAuthorization("AdminOnly");

        group.MapDelete("/{trackingNumber}", async (HttpContext http, string trackingNumber,
            ShipmentService service, CancellationToken ct) =>
        {
            if (Actor.From(http.User) is not { } actor) return Results.Unauthorized();

            var result = await service.DeleteAsync(actor, trackingNumber, ct);
            return result.Kind == ResultKind.Ok ? Results.NoContent() : result.ToProblem();
        })
        .RequireAuthorization("AdminOnly");

        return app;
    }
}
