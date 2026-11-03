var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Cargo tracking API is running. See /api/health for a health check.");
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
