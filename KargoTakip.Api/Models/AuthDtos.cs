namespace KargoTakip.Api.Models;

public record RegisterRequest(string? Username, string? DisplayName, string? Password);

public record LoginRequest(string? Username, string? Password);

/// <summary>The signed-in user as the front end needs to see it (never includes the password hash).</summary>
public record MeDto(Guid Id, string Username, string DisplayName, UserRole Role, Guid? CourierId);
