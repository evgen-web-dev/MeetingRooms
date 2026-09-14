using MeetingRooms.Application.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MeetingRooms.ConcurrencyTests;

/// <summary>
/// Hosts the real application in memory - real controllers, real EF, real Identity, real JWT -
/// against a real SQL Server. Only configuration is substituted.
/// </summary>
public sealed class BookingApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// A catalog of its own, and not negotiable. Bookings cannot be cancelled, so a run against
    /// the development database would permanently consume demo slots with no way to give them
    /// back.
    /// </summary>
    public const string TestCatalog = "MeetingRooms_Tests";

    private static readonly string TestSettingsPath =
        Path.Combine(AppContext.BaseDirectory, "appsettings.Tests.json");

    /// <summary>
    /// <c>appsettings.Tests.json</c> supplies a localhost default matching the repository root's
    /// <c>docker-compose.yml</c>; an environment variable overrides it, which is what happens
    /// inside the dev container.
    /// </summary>
    private static readonly IConfiguration TestConfiguration = new ConfigurationBuilder()
        .AddJsonFile(TestSettingsPath, optional: false)
        .AddEnvironmentVariables()
        .Build();

    /// <summary>
    /// Whatever is configured, with the catalog replaced. Deriving it rather than writing one out
    /// is what keeps a credential out of this repository while still letting the tests run in two
    /// very different environments.
    /// </summary>
    public static string ConnectionString { get; } =
        new SqlConnectionStringBuilder(
            TestConfiguration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "No connection string. Set ConnectionStrings__DefaultConnection, or run "
                + "`docker compose up -d` at the repository root to use the default in "
                + "appsettings.Tests.json."))
        {
            InitialCatalog = TestCatalog
        }.ConnectionString;

    public static string AdminEmail { get; } = Required($"{SeedOptions.SectionName}:AdminEmail");

    public static string AdminPassword { get; } = Required($"{SeedOptions.SectionName}:AdminPassword");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development, so the administrator seeder warns rather than refusing to boot if its
        // configuration is ever missing - and so the failure is a readable assertion here rather
        // than a startup crash.
        builder.UseEnvironment(Environments.Development);

        // Added last, so these win over the API project's own appsettings.json and over the
        // environment variable the dev container sets. The catalog override is the whole point:
        // without it the dev container's variable would point these tests at the development
        // database.
        builder.ConfigureAppConfiguration(configuration => configuration
            .AddJsonFile(TestSettingsPath, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString
            }));
    }

    private static string Required(string key) =>
        TestConfiguration[key] ?? throw new InvalidOperationException($"'{key}' is missing from appsettings.Tests.json.");
}
