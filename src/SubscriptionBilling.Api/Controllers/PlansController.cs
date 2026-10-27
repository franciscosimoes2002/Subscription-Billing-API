using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubscriptionBilling.Api.Http;
using SubscriptionBilling.Application.Common;
using SubscriptionBilling.Application.Plans;

namespace SubscriptionBilling.Api.Controllers;

[Route("api/v1/plans")]
public sealed class PlansController(PlanService plans) : ApiControllerBase
{
    /// <summary>The public catalogue: plans that can be subscribed to.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<PlanDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = PageRequest.DefaultPageSize) =>
        Ok(await plans.ListAsync(Page(page, pageSize), cancellationToken));

    /// <summary>Back-office catalogue, including deactivated plans.</summary>
    [HttpGet("all")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<PagedResult<PlanDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAll(CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = PageRequest.DefaultPageSize) =>
        Ok(await plans.ListAllAsync(Page(page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<PlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Respond(await plans.GetAsync(id, includeInactive: false, cancellationToken), Ok);

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<PlanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(PlanRequest request, CancellationToken cancellationToken) =>
        Respond(await plans.CreateAsync(request, cancellationToken), plan => CreatedAtAction(nameof(Get), new { id = plan.Id }, plan));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<PlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, PlanRequest request, CancellationToken cancellationToken) =>
        Respond(await plans.UpdateAsync(id, request, cancellationToken), Ok);

    /// <summary>Soft delete: existing subscriptions keep their price, new ones are no longer possible.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        Respond(await plans.DeleteAsync(id, cancellationToken), _ => NoContent());
}
