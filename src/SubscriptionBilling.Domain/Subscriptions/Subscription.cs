using SubscriptionBilling.Domain.Common;
using SubscriptionBilling.Domain.Plans;

namespace SubscriptionBilling.Domain.Subscriptions;

public sealed class Subscription : Entity
{
    private Subscription()
    {
        // Required by EF Core.
    }

    public Guid UserId { get; private set; }

    public Guid PlanId { get; private set; }

    /// <summary>Plan name, price and currency are copied when the subscription starts, so later plan edits never rewrite history.</summary>
    public string PlanName { get; private set; } = string.Empty;

    public long PriceCents { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public BillingInterval Interval { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime CurrentPeriodEndUtc { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public static Subscription Start(Guid userId, Plan plan, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (userId == Guid.Empty)
        {
            throw DomainException.Validation("invalid_user", "A subscription needs a user.");
        }

        if (!plan.CanBeSubscribed)
        {
            throw DomainException.Conflict("plan_unavailable", "The plan is not available for new subscriptions.");
        }

        return new Subscription
        {
            UserId = userId,
            PlanId = plan.Id,
            PlanName = plan.Name,
            PriceCents = plan.PriceCents,
            Currency = plan.Currency,
            Interval = plan.Interval,
            Status = SubscriptionStatus.Active,
            StartedAtUtc = nowUtc,
            CurrentPeriodEndUtc = NextPeriodEnd(nowUtc, plan.Interval),
            CreatedAtUtc = nowUtc,
        };
    }

    public void Cancel(DateTime nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status == SubscriptionStatus.Cancelled)
        {
            throw DomainException.Conflict("subscription_already_cancelled", "The subscription is already cancelled.");
        }

        Status = SubscriptionStatus.Cancelled;
        CancelledAtUtc = nowUtc;
    }

    public static DateTime NextPeriodEnd(DateTime fromUtc, BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly => fromUtc.AddMonths(1),
        BillingInterval.Yearly => fromUtc.AddYears(1),
        _ => throw DomainException.Validation("invalid_interval", "The billing interval is not valid."),
    };
}
