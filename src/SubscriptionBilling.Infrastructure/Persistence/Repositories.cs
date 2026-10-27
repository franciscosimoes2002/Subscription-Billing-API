using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SubscriptionBilling.Application.Abstractions;
using SubscriptionBilling.Application.Common;
using SubscriptionBilling.Domain.Plans;
using SubscriptionBilling.Domain.Subscriptions;
using SubscriptionBilling.Domain.Users;

namespace SubscriptionBilling.Infrastructure.Persistence;

internal sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(x => x.Email == normalizedEmail, cancellationToken);

    public void Add(User user) => db.Users.Add(user);
}

internal sealed class PlanRepository(AppDbContext db) : IPlanRepository
{
    public Task<Plan?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Plans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<PagedResult<Plan>> ListAsync(PageRequest page, bool activeOnly, CancellationToken cancellationToken)
    {
        var query = db.Plans.AsNoTracking().Where(x => !activeOnly || x.IsActive);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.PriceCents).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Plan>(items, page.Page, page.PageSize, total);
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken cancellationToken) =>
        db.Plans.AnyAsync(x => x.Name == name && (excludingId == null || x.Id != excludingId), cancellationToken);

    public void Add(Plan plan) => db.Plans.Add(plan);
}

internal sealed class SubscriptionRepository(AppDbContext db) : ISubscriptionRepository
{
    public Task<Subscription?> FindForUserAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken) =>
        db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId && x.UserId == userId, cancellationToken);

    public Task<bool> HasActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Subscriptions.AnyAsync(x => x.UserId == userId && x.Status == SubscriptionStatus.Active, cancellationToken);

    public async Task<PagedResult<Subscription>> ListByUserAsync(Guid userId, PageRequest page, CancellationToken cancellationToken)
    {
        var query = db.Subscriptions.AsNoTracking().Where(x => x.UserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Subscription>(items, page.Page, page.PageSize, total);
    }

    public void Add(Subscription subscription) => db.Subscriptions.Add(subscription);
}

internal sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new UniqueConstraintViolationException(exception);
        }
    }

    // SQL Server reports 2601 (unique index) or 2627 (unique constraint). SQLite, used by the test suite, reports a message.
    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        SqlException { Number: 2601 or 2627 } => true,
        { } inner => inner.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
