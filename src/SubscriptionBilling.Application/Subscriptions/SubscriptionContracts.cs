using SubscriptionBilling.Domain.Subscriptions;

namespace SubscriptionBilling.Application.Subscriptions;

public sealed record SubscribeRequest(Guid PlanId);

public sealed record SubscriptionDto(
    Guid Id,
    Guid PlanId,
    string PlanName,
    long PriceCents,
    string Currency,
    string Interval,
    string Status,
    DateTime StartedAtUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime? CancelledAtUtc)
{
    public static SubscriptionDto From(Subscription s) => new(
        s.Id, s.PlanId, s.PlanName, s.PriceCents, s.Currency, s.Interval.ToString(), s.Status.ToString(), s.StartedAtUtc, s.CurrentPeriodEndUtc, s.CancelledAtUtc);
}
