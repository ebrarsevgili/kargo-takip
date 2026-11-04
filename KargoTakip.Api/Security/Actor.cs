using System.Security.Claims;
using KargoTakip.Api.Models;

namespace KargoTakip.Api.Security;

/// <summary>
/// "Who is making this request": the signed-in user as the services see it. Built from the claims stored in the
/// auth cookie, so services never depend on ASP.NET types and are easy to reason about.
/// </summary>
public sealed record Actor(Guid UserId, string Username, string DisplayName, UserRole Role, Guid? CourierId)
{
    private const string DisplayNameClaim = "display_name";
    private const string CourierIdClaim = "courier_id";

    public static ClaimsPrincipal ToPrincipal(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(DisplayNameClaim, user.DisplayName)
        };
        if (user.CourierId is { } courierId) claims.Add(new Claim(CourierIdClaim, courierId.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "cookie"));
    }

    /// <summary>Returns null when the principal is not signed in or its claims are malformed.</summary>
    public static Actor? From(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return null;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        if (!Enum.TryParse<UserRole>(principal.FindFirstValue(ClaimTypes.Role), out var role)) return null;

        Guid? courierId = Guid.TryParse(principal.FindFirstValue(CourierIdClaim), out var c) ? c : null;
        return new Actor(id, principal.Identity.Name ?? "", principal.FindFirstValue(DisplayNameClaim) ?? "", role, courierId);
    }
}
