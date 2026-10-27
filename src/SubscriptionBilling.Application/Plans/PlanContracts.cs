using System.ComponentModel.DataAnnotations;
using SubscriptionBilling.Domain.Plans;

namespace SubscriptionBilling.Application.Plans;

public sealed record PlanRequest(
    [Required, StringLength(Plan.NameMaxLength, MinimumLength = 2)] string Name,
    [StringLength(Plan.DescriptionMaxLength)] string? Description,
    [Range(0, Plan.MaxPriceCents)] long PriceCents,
    [Required, StringLength(3, MinimumLength = 3)] string Currency,
    BillingInterval Interval);

public sealed record PlanDto(Guid Id, string Name, string Description, long PriceCents, string Currency, string Interval, bool IsActive)
{
    public static PlanDto From(Plan plan) => new(plan.Id, plan.Name, plan.Description, plan.PriceCents, plan.Currency, plan.Interval.ToString(), plan.IsActive);
}
