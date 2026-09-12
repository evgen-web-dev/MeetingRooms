using MeetingRooms.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddExceptionHandlersWithProblemDetails();

// The composition root owns the clock. Nothing below reads DateTime.UtcNow directly,
// so time can be substituted in a test without reaching for a static.
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// First in the pipeline, so it sees every exception thrown by anything below it.
app.UseExceptionHandler();

// Order matters. UseDefaultFiles rewrites "/" to "/index.html";
// UseStaticFiles then serves it from wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// An unmatched API route must fail as an API. Without this, MapFallbackToFile below
// answers "/api/typo" with 200 and index.html, which is the wrong answer under review.
// Safe: a literal controller route outranks a catch-all parameter, so real endpoints win.
app.Map("/api/{**slug}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound));

// Unmatched requests return index.html so client-side routes survive a hard refresh.
app.MapFallbackToFile("index.html");

app.Run();
