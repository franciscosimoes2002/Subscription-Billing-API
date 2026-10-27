using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SubscriptionBilling.Application.Common;

namespace SubscriptionBilling.Api.Http;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue("sub"), out var id)
            ? id
            : throw new InvalidOperationException("An authenticated request is missing the subject claim.");

    protected IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        return result.IsSuccess ? onSuccess(result.Value) : Fail(result.Failure!);
    }

    protected IActionResult Fail(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var status = failure.Kind switch
        {
            FailureKind.Validation => StatusCodes.Status400BadRequest,
            FailureKind.NotFound => StatusCodes.Status404NotFound,
            FailureKind.Conflict => StatusCodes.Status409Conflict,
            FailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
            FailureKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, detail: failure.Message);
        problem.Extensions["code"] = failure.Code;
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected static PageRequest Page(int page, int pageSize) => new(page, pageSize);
}
