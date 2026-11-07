using KargoTakip.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KargoTakip.Api.Storage;

/// <summary>
/// The SQLite schema. The tables are described here in code (instead of attributes on the models) so the
/// models stay plain classes that the JSON storage uses as well.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Courier> Couriers => Set<Courier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shipment>(shipment =>
        {
            shipment.HasKey(s => s.TrackingNumber);
            shipment.Property(s => s.TrackingNumber).HasMaxLength(12);
            shipment.Property(s => s.SenderName).HasMaxLength(100);
            shipment.Property(s => s.ReceiverName).HasMaxLength(100);
            shipment.Property(s => s.ReceiverPhone).HasMaxLength(20);
            shipment.Property(s => s.OriginCity).HasMaxLength(80);
            shipment.Property(s => s.DestinationCity).HasMaxLength(80);
            shipment.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);   // stored as "InTransit", not 2
            shipment.Ignore(s => s.IsClosed);                                            // computed from Status

            shipment.HasIndex(s => s.OwnerUserId);
            shipment.HasIndex(s => s.CourierId);
            shipment.HasIndex(s => s.Status);

            // The movements belong to one shipment and have no meaning on their own: an owned collection,
            // stored in its own table and deleted together with the shipment.
            shipment.OwnsMany(s => s.Events, events =>
            {
                events.ToTable("ShipmentEvents");
                events.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
                events.Property(e => e.Location).HasMaxLength(100);
                events.Property(e => e.Note).HasMaxLength(300);
            });
        });

        modelBuilder.Entity<AppUser>(user =>
        {
            user.HasKey(u => u.Id);
            user.Property(u => u.Username).HasMaxLength(30);
            user.Property(u => u.DisplayName).HasMaxLength(60);
            user.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
            user.HasIndex(u => u.Username).IsUnique();   // user names are always stored in lower case
            user.HasIndex(u => u.CourierId);
        });

        modelBuilder.Entity<Vehicle>(vehicle =>
        {
            vehicle.HasKey(v => v.Id);
            vehicle.Property(v => v.Plate).HasMaxLength(12);
            vehicle.Property(v => v.Type).HasMaxLength(20);
            vehicle.HasIndex(v => v.Plate).IsUnique();   // plates are stored normalized (upper case, no spaces)
        });

        modelBuilder.Entity<Courier>(courier =>
        {
            courier.HasKey(c => c.Id);
            courier.Property(c => c.Name).HasMaxLength(100);
            courier.Property(c => c.Phone).HasMaxLength(20);
        });
    }
}
