using MeetingRooms.Api;
using MeetingRooms.Api.Filters;
using MeetingRooms.Api.Hubs;
using MeetingRooms.Application;
using MeetingRooms.Infrastructure;
using MeetingRooms.Infrastructure.Persistence;
using MeetingRooms.Infrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// One filter registration covers every endpoint, so no controller can forget to validate.
builder.Services.AddControllers(options => options.Filters.Add<AsyncValidationFilter>());
builder.Services.AddExceptionHandlersWithProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddRealtime(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructurePersistence(builder.Configuration);
builder.Services.AddInfrastructureServices();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAppValidation();

// The composition root owns the clock. Nothing below reads DateTime.UtcNow directly,
// so time can be substituted in a test without reaching for a static.
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// Schema first, then reference data, then the account that needs it: seeding a role into a
// database with no tables fails, and an administrator cannot be granted a role that does not
// exist yet. Applying migrations on start is safe because the deployment is a single instance
// - see docs/decisions.md - and every step below is idempotent, so a restart is a no-op.
await using (var startupScope = app.Services.CreateAsyncScope())
{
    await startupScope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

    await IdentitySeeder.SeedRolesAsync(startupScope.ServiceProvider);
    await IdentitySeeder.SeedAdminAsync(startupScope.ServiceProvider, app.Environment.IsDevelopment());
}

// First in the pipeline, so it sees every exception thrown by anything below it.
app.UseExceptionHandler();

// Order matters. UseDefaultFiles rewrites "/" to "/index.html";
// UseStaticFiles then serves it from wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

// After the static files above: the SPA bundle is public, and only endpoints below this
// point are ever gated. Authentication identifies the caller; authorization decides.
app.UseAuthentication();
app.UseAuthorization();

// Deliberately not gated to Development: a reviewer must be able to exercise the API
// on the deployed URL without cloning anything. Nothing secret is in the document.
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    // The reference page is public, so it makes no calls we did not ask for:
    // no usage reporting, no agent panel, and no fonts fetched from an external host.
    // Disabling the fonts also makes the page render identically inside the dev
    // container, whose firewall would block them.
    options.DisableTelemetry();
    options.DisableAgent();
    options.DisableDefaultFonts();
});

app.MapControllers();
app.MapHub<ScheduleHub>("/hubs/schedule");

// An unmatched API route must fail as an API. Without this, MapFallbackToFile below
// answers "/api/typo" with 200 and index.html, which is the wrong answer under review.
// Safe: a literal controller route outranks a catch-all parameter, so real endpoints win.
app.Map("/api/{**slug}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound));

// Unmatched requests return index.html so client-side routes survive a hard refresh.
app.MapFallbackToFile("index.html");

app.Run();
