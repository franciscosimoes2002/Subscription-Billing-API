using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SubscriptionBilling.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c> to build migrations. It never runs inside the application.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // Design-time placeholder: migrations are generated from the model and do not connect to this server.
    private const string DesignTimeConnection = "Server=(localdb)\\mssqllocaldb;Database=SubscriptionBillingDesign;Trusted_Connection=True;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? DesignTimeConnection;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        return new AppDbContext(options);
    }
}
