using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KargoTakip.Api.Storage;

/// <summary>
/// Used only by the "dotnet ef" command line tool (for example "dotnet ef migrations add ...").
/// The tool never opens the database for that, so the file name here does not matter.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
