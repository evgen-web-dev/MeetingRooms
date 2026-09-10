var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Order matters. UseDefaultFiles rewrites "/" to "/index.html";
// UseStaticFiles then serves it from wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", (IConfiguration config, IWebHostEnvironment env) =>
{
    // Only report whether the connection string is present.
    // Never return or log the value itself.
    var connectionString = config.GetConnectionString("DefaultConnection");

    return Results.Ok(new
    {
        status = "ok",
        environment = env.EnvironmentName,
        utc = DateTime.UtcNow,
        connectionStringConfigured = !string.IsNullOrWhiteSpace(connectionString)
    });
});

// Unmatched requests return index.html so client-side routes survive a hard refresh.
app.MapFallbackToFile("index.html");

app.Run();