using Microsoft.Extensions.Logging;
using SubscriptionBilling.Application.Abstractions;
using SubscriptionBilling.Application.Common;
using SubscriptionBilling.Domain.Subscriptions;

namespace SubscriptionBilling.Application.Subscriptions;

public sealed partial class SubscriptionService(
    IPlanRepository plans,
    ISubscriptionRepository subscriptions,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<SubscriptionService> logger)
{
    public async Task<Result<SubscriptionDto>> SubscribeAsync(Guid userId, Guid planId, CancellationToken cancellationToken)
    {
        var plan = await plans.FindByIdAsync(planId, cancellationToken);
        if (plan is null || !plan.CanBeSubscribed)
        {
            // An inactive plan looks exactly like a missing one: the catalogue never leaks plans that are not on sale.
            return Failure.NotFound("plan_not_found", "The plan does not exist.");
        }

        if (await subscriptions.HasActiveAsync(userId, cancellationToken))
        {
            return AlreadySubscribed();
        }

        var subscription = Subscription.Start(userId, plan, time.GetUtcNow().UtcDateTime);
        subscriptions.Add(subscription);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Two concurrent requests passed the check above; the filtered unique index let only one through.
            return AlreadySubscribed();
        }

        LogSubscribed(subscription.Id, plan.Id);
        return SubscriptionDto.From(subscription);
    }

    public async Task<Result<SubscriptionDto>> CancelAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken)
    {
        // Looked up by owner: someone else's subscription is reported as missing, never as forbidden.
        var subscription = await subscriptions.FindForUserAsync(userId, subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return SubscriptionNotFound();
        }

        subscription.Cancel(time.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogCancelled(subscription.Id);
        return SubscriptionDto.From(subscription);
    }

    public async Task<Result<SubscriptionDto>> GetAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken)
    {
        var subscription = await subscriptions.FindForUserAsync(userId, subscriptionId, cancellationToken);
        return subscription is null ? SubscriptionNotFound() : SubscriptionDto.From(subscription);
    }

    public async Task<PagedResult<SubscriptionDto>> ListAsync(Guid userId, PageRequest page, CancellationToken cancellationToken) =>
        (await subscriptions.ListByUserAsync(userId, page, cancellationToken)).Map(SubscriptionDto.From);

    private static Failure AlreadySubscribed() =>
        Failure.Conflict("already_subscribed", "The account already has an active subscription. Cancel it before choosing another plan.");

    private static Failure SubscriptionNotFound() => Failure.NotFound("subscription_not_found", "The subscription does not exist.");

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Subscription started {SubscriptionId} on plan {PlanId}")]
    private partial void LogSubscribed(Guid subscriptionId, Guid planId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Subscription cancelled {SubscriptionId}")]
    private partial void LogCancelled(Guid subscriptionId);
}
