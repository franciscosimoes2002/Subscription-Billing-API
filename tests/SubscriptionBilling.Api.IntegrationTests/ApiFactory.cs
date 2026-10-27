using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SubscriptionBilling.Infrastructure.Persistence;

namespace SubscriptionBilling.Api.IntegrationTests;

/// <summary>
/// Boots the real application (middleware, auth, controllers, EF Core) against a throw-away SQLite file.
/// A file rather than a shared in-memory connection, so concurrent requests each get their own connection like in production.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.test";
    public const string AdminPassword = "an administrator passphrase";
    public const string UserPassword = "a perfectly fine passphrase";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"subscription-billing-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Server=unused;Database=unused"); // Replaced by SQLite below.
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("Database:Initialization", "EnsureCreated");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("OpenApi:Enabled", "true");

        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            // EF Core 9+ keeps provider configuration in IDbContextOptionsConfiguration<T>: remove both registrations.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools(); // Release the file handle so the temp database can be deleted.
        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm", $"{_databasePath}-journal" })
        {
            File.Delete(path);
        }
    }
}

internal static class TestClient
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    public static async Task<string> LoginAsync(this HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginBody>(Ct);
        return body!.AccessToken;
    }

    /// <summary>Registers a fresh account and returns a client that sends its bearer token.</summary>
    public static async Task<HttpClient> NewUserClientAsync(this ApiFactory factory, string? email = null)
    {
        email ??= NewEmail();
        var anonymous = factory.CreateClient();
        var register = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new { email, fullName = "Test User", password = ApiFactory.UserPassword }, Ct);
        register.EnsureSuccessStatusCode();
        return factory.ClientWith(await anonymous.LoginAsync(email, ApiFactory.UserPassword));
    }

    public static async Task<HttpClient> NewAdminClientAsync(this ApiFactory factory) =>
        factory.ClientWith(await factory.CreateClient().LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword));

    public static HttpClient ClientWith(this ApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<PlanBody> CreatePlanAsync(this HttpClient admin, string? name = null, long priceCents = 1999)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/plans",
            new { name = name ?? $"Plan {Guid.NewGuid():N}", description = "test plan", priceCents, currency = "EUR", interval = "Monthly" },
            Ct);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlanBody>(Ct))!;
    }

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Ct);
        return problem?.Code;
    }
}

internal sealed record LoginBody(string AccessToken, string TokenType, DateTime ExpiresAtUtc);

internal sealed record PlanBody(Guid Id, string Name, long PriceCents, string Currency, string Interval, bool IsActive);

internal sealed record SubscriptionBody(Guid Id, Guid PlanId, string PlanName, long PriceCents, string Status);

internal sealed record PageBody<T>(List<T> Items, int Page, int PageSize, int TotalCount);

internal sealed record ProblemBody(int Status, string? Detail, string? Code, string? CorrelationId);
