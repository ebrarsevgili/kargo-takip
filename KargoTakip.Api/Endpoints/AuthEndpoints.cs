using System.Security.Claims;
using KargoTakip.Api.Models;
using KargoTakip.Api.Security;
using KargoTakip.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace KargoTakip.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Sign-in attempts are rate limited (see the "auth" policy in Program.cs) against password guessing.
        var group = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        group.MapPost("/register", async (RegisterRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            var result = await auth.RegisterAsync(request, ct);
            if (result.Kind != ResultKind.Ok) return result.ToProblem();

            await SignInAsync(http, result.Value!);
            return Results.Ok(ToMe(result.Value!));
        });

        group.MapPost("/login", async (LoginRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            var user = await auth.ValidateCredentialsAsync(request.Username, request.Password, ct);
            if (user is null)
                return Results.Problem(title: "Giriş başarısız", detail: "Kullanıcı adı veya şifre hatalı.", statusCode: StatusCodes.Status401Unauthorized);

            await SignInAsync(http, user);
            return Results.Ok(ToMe(user));
        });

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        // Lets the front end ask "am I signed in, and as whom?"
        group.MapGet("/me", (ClaimsPrincipal principal) =>
            Actor.From(principal) is { } actor
                ? Results.Ok(new MeDto(actor.UserId, actor.Username, actor.DisplayName, actor.Role, actor.CourierId))
                : Results.Unauthorized())
            .RequireAuthorization();
    }

    private static Task SignInAsync(HttpContext http, AppUser user) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Actor.ToPrincipal(user));

    private static MeDto ToMe(AppUser user) => new(user.Id, user.Username, user.DisplayName, user.Role, user.CourierId);
}
