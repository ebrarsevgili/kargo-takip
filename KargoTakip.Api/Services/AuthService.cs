using System.Text.RegularExpressions;
using KargoTakip.Api.Models;
using KargoTakip.Api.Security;
using KargoTakip.Api.Storage;

namespace KargoTakip.Api.Services;

/// <summary>Creating accounts and checking credentials.</summary>
public sealed partial class AuthService
{
    private readonly IUserRepository _users;
    private readonly TimeProvider _time;

    // Makes "check the username is free, then add the user" one atomic step.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // Verified against when the username does not exist, so the response time does not reveal which usernames exist.
    private static readonly string DummyHash = PasswordHasher.Hash("not-a-real-password-1");

    public AuthService(IUserRepository users, TimeProvider time)
    {
        _users = users;
        _time = time;
    }

    [GeneratedRegex("^[a-z0-9._-]{3,30}$")]
    private static partial Regex UsernameFormat();

    /// <summary>Public sign-up always creates a Customer. Other roles are created by an admin.</summary>
    public Task<ServiceResult<AppUser>> RegisterAsync(RegisterRequest request, CancellationToken ct) =>
        CreateUserAsync(request.Username, request.DisplayName, request.Password, UserRole.Customer, null, ct);

    public async Task<ServiceResult<AppUser>> CreateUserAsync(
        string? username, string? displayName, string? password, UserRole role, Guid? courierId, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = username?.Trim().ToLowerInvariant();
        var display = displayName?.Trim();

        if (name is null || !UsernameFormat().IsMatch(name))
            errors["Username"] = ["Kullanıcı adı 3-30 karakter olmalı; yalnızca harf (a-z), rakam, nokta, tire ve alt çizgi içerebilir."];
        if (string.IsNullOrEmpty(display) || display.Length is < 2 or > 60)
            errors["DisplayName"] = ["Ad 2-60 karakter olmalı."];
        if (password is null || password.Length is < 8 or > 128 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            errors["Password"] = ["Şifre 8-128 karakter olmalı ve en az bir harf ile bir rakam içermeli."];

        if (errors.Count > 0) return ServiceResult<AppUser>.Invalid(errors);

        await Gate.WaitAsync(ct);
        try
        {
            if (await _users.GetByUsernameAsync(name!, ct) is not null)
                return ServiceResult<AppUser>.Conflict("Bu kullanıcı adı zaten alınmış.");

            var user = new AppUser
            {
                Username = name!,
                DisplayName = display!,
                PasswordHash = PasswordHasher.Hash(password!),
                Role = role,
                CourierId = courierId,
                CreatedAt = _time.GetUtcNow()
            };
            await _users.AddAsync(user, ct);
            return ServiceResult<AppUser>.Ok(user);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Returns the user when the credentials are right, otherwise null (without saying which part was wrong).</summary>
    public async Task<AppUser?> ValidateCredentialsAsync(string? username, string? password, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(username) ? null : await _users.GetByUsernameAsync(username.Trim(), ct);
        var passwordOk = PasswordHasher.Verify(password ?? "", user?.PasswordHash ?? DummyHash);
        return passwordOk && user is not null ? user : null;
    }

    /// <summary>On the very first start (no users yet) creates the admin account from configuration.</summary>
    public async Task SeedAdminAsync(string? username, string? password, ILogger logger)
    {
        if (await _users.AnyAsync()) return;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("No users exist and Admin:Username / Admin:Password are not configured, so nobody can sign in as admin.");
            return;
        }

        var result = await CreateUserAsync(username, "Administrator", password, UserRole.Admin, null, CancellationToken.None);
        if (result.Kind == ResultKind.Ok)
            logger.LogInformation("Created the initial admin account '{Username}'.", result.Value!.Username);
        else
            logger.LogWarning("Could not create the initial admin account: {Errors}",
                string.Join(" ", result.Errors?.Values.SelectMany(v => v) ?? [result.Message ?? "unknown error"]));
    }
}
