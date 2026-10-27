using Microsoft.AspNetCore.Mvc;
using SubscriptionBilling.Api.Http;
using SubscriptionBilling.Application.Common;
using SubscriptionBilling.Application.Subscriptions;

namespace SubscriptionBilling.Api.Controllers;

/// <summary>Every operation is scoped to the authenticated user: there is no way to name someone else's subscription.</summary>
[Route("api/v1/subscriptions")]
public sealed class SubscriptionsController(SubscriptionService subscriptions) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType<SubscriptionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Subscribe(SubscribeRequest request, CancellationToken cancellationToken) =>
        Respond(
            await subscriptions.SubscribeAsync(CurrentUserId, request.PlanId, cancellationToken),
            subscription => CreatedAtAction(nameof(Get), new { id = subscription.Id }, subscription));

    [HttpGet]
    [ProducesResponseType<PagedResult<SubscriptionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = PageRequest.DefaultPageSize) =>
        Ok(await subscriptions.ListAsync(CurrentUserId, Page(page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<SubscriptionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Respond(await subscriptions.GetAsync(CurrentUserId, id, cancellationToken), Ok);

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<SubscriptionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        Respond(await subscriptions.CancelAsync(CurrentUserId, id, cancellationToken), Ok);
}
