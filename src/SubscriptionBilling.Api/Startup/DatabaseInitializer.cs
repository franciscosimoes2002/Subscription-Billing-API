using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SubscriptionBilling.Api.Options;
using SubscriptionBilling.Application.Abstractions;
using SubscriptionBilling.Application.Auth;
using SubscriptionBilling.Domain.Users;
using SubscriptionBilling.Infrastructure.Persistence;

namespace SubscriptionBilling.Api.Startup;

internal static partial class DatabaseInitializer
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var db = provider.GetRequiredService<AppDbContext>();

        switch (database.Initialization)
        {
            case DatabaseInitialization.Migrate:
                await db.Database.MigrateAsync(cancellationToken);
                LogInitialized(logger, database.Initialization);
                break;
            case DatabaseInitialization.EnsureCreated:
                await db.Database.EnsureCreatedAsync(cancellationToken);
                LogInitialized(logger, database.Initialization);
                break;
            case DatabaseInitialization.None:
            default:
                return;
        }

        await SeedAdministratorAsync(provider, logger, cancellationToken);
    }

    private static async Task SeedAdministratorAsync(IServiceProvider provider, ILogger logger, CancellationToken cancellationToken)
    {
        var seed = provider.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (string.IsNullOrWhiteSpace(seed.AdminEmail) || string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            return;
        }

        if (!PasswordPolicy.IsAcceptable(seed.AdminPassword))
        {
            throw new InvalidOperationException($"Seed:AdminPassword must have between {PasswordPolicy.MinLength} and {PasswordPolicy.MaxLength} characters.");
        }

        var users = provider.GetRequiredService<IUserRepository>();
        if (await users.EmailExistsAsync(User.NormalizeEmail(seed.AdminEmail), cancellationToken))
        {
            return;
        }

        var hasher = provider.GetRequiredService<IPasswordHasher>();
        var time = provider.GetRequiredService<TimeProvider>();
        users.Add(User.Register(seed.AdminEmail, "Administrator", hasher.Hash(seed.AdminPassword), UserRole.Admin, time.GetUtcNow().UtcDateTime));
        await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        LogAdminSeeded(logger);
    }

    [LoggerMessage(EventId = 8001, Level = LogLevel.Information, Message = "Database initialised using {Mode}")]
    private static partial void LogInitialized(ILogger logger, DatabaseInitialization mode);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Information, Message = "Administrator account created from configuration")]
    private static partial void LogAdminSeeded(ILogger logger);
}
