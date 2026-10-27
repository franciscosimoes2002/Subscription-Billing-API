using SubscriptionBilling.Application.Common;
using SubscriptionBilling.Domain.Plans;
using SubscriptionBilling.Domain.Subscriptions;
using SubscriptionBilling.Domain.Users;

namespace SubscriptionBilling.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);

    void Add(User user);
}

public interface IPlanRepository
{
    Task<Plan?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<Plan>> ListAsync(PageRequest page, bool activeOnly, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken cancellationToken);

    void Add(Plan plan);
}

public interface ISubscriptionRepository
{
    Task<Subscription?> FindForUserAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken);

    Task<bool> HasActiveAsync(Guid userId, CancellationToken cancellationToken);

    Task<PagedResult<Subscription>> ListByUserAsync(Guid userId, PageRequest page, CancellationToken cancellationToken);

    void Add(Subscription subscription);
}

public interface IUnitOfWork
{
    /// <exception cref="UniqueConstraintViolationException">A unique index rejected the change (a concurrent request won the race).</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class UniqueConstraintViolationException(Exception inner)
    : Exception("A unique constraint was violated.", inner);

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Verifies in constant time. A null hash (unknown user) still spends the same work, so response time does not reveal
    /// whether an account exists.
    /// </summary>
    bool Verify(string? hash, string password);
}

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessToken Issue(User user, DateTime nowUtc);
}
