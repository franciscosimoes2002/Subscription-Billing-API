using Microsoft.Extensions.Logging;
using SubscriptionBilling.Application.Abstractions;
using SubscriptionBilling.Application.Common;
using SubscriptionBilling.Domain.Plans;

namespace SubscriptionBilling.Application.Plans;

public sealed partial class PlanService(
    IPlanRepository plans,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<PlanService> logger)
{
    /// <summary>Catalogue for everyone: only plans that can be subscribed to.</summary>
    public async Task<PagedResult<PlanDto>> ListAsync(PageRequest page, CancellationToken cancellationToken) =>
        (await plans.ListAsync(page, activeOnly: true, cancellationToken)).Map(PlanDto.From);

    /// <summary>Back-office view: includes deactivated plans.</summary>
    public async Task<PagedResult<PlanDto>> ListAllAsync(PageRequest page, CancellationToken cancellationToken) =>
        (await plans.ListAsync(page, activeOnly: false, cancellationToken)).Map(PlanDto.From);

    public async Task<Result<PlanDto>> GetAsync(Guid id, bool includeInactive, CancellationToken cancellationToken)
    {
        var plan = await plans.FindByIdAsync(id, cancellationToken);
        return plan is null || (!includeInactive && !plan.IsActive) ? PlanNotFound() : PlanDto.From(plan);
    }

    public async Task<Result<PlanDto>> CreateAsync(PlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = Plan.Create(request.Name, request.Description, request.PriceCents, request.Currency, request.Interval, time.GetUtcNow().UtcDateTime);
        if (await plans.NameExistsAsync(plan.Name, excludingId: null, cancellationToken))
        {
            return NameTaken();
        }

        plans.Add(plan);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return NameTaken();
        }

        LogPlanCreated(plan.Id);
        return PlanDto.From(plan);
    }

    public async Task<Result<PlanDto>> UpdateAsync(Guid id, PlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = await plans.FindByIdAsync(id, cancellationToken);
        if (plan is null)
        {
            return PlanNotFound();
        }

        plan.Update(request.Name, request.Description, request.PriceCents, request.Currency, request.Interval);
        if (await plans.NameExistsAsync(plan.Name, excludingId: plan.Id, cancellationToken))
        {
            return NameTaken();
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return NameTaken();
        }

        LogPlanUpdated(plan.Id);
        return PlanDto.From(plan);
    }

    /// <summary>Soft delete: existing subscriptions keep their price snapshot, but nobody can subscribe any more.</summary>
    public async Task<Result<Unit>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await plans.FindByIdAsync(id, cancellationToken);
        if (plan is null)
        {
            return PlanNotFound();
        }

        plan.Deactivate();
        plan.SoftDelete(time.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogPlanDeleted(plan.Id);
        return new Unit();
    }

    private static Failure PlanNotFound() => Failure.NotFound("plan_not_found", "The plan does not exist.");

    private static Failure NameTaken() => Failure.Conflict("plan_name_taken", "A plan with this name already exists.");

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Plan created {PlanId}")]
    private partial void LogPlanCreated(Guid planId);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Plan updated {PlanId}")]
    private partial void LogPlanUpdated(Guid planId);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "Plan deleted {PlanId}")]
    private partial void LogPlanDeleted(Guid planId);
}
