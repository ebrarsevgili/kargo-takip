namespace KargoTakip.Api.Models;

public enum UserRole
{
    Admin,      // manages everything
    Courier,    // updates the shipments assigned to them
    Customer    // creates and follows their own shipments
}

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public Guid? CourierId { get; set; }   // only set for users with the Courier role
    public DateTimeOffset CreatedAt { get; set; }
}
