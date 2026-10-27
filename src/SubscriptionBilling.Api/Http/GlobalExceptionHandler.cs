using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SubscriptionBilling.Domain.Common;

namespace SubscriptionBilling.Api.Http;

/// <summary>
/// The only place that turns exceptions into responses. Business-rule violations become 400/409 with a stable code;
/// everything else is a generic 500: the detail goes to the log (with the correlation id), never to the client.
/// </summary>
public sealed partial class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499; // The client went away; nobody is listening for a body.
            return true;
        }

        var problem = new ProblemDetails();
        if (exception is DomainException domain)
        {
            problem.Status = domain.Kind == DomainErrorKind.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            problem.Detail = domain.Message;
            problem.Extensions["code"] = domain.Code;
            LogDomainRule(domain.Code);
        }
        else
        {
            problem.Status = StatusCodes.Status500InternalServerError;
            problem.Detail = "An unexpected error occurred. Quote the correlation id when reporting it.";
            problem.Extensions["code"] = "internal_error";
            LogUnhandled(exception);
        }

        httpContext.Response.StatusCode = problem.Status.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
    }

    [LoggerMessage(EventId = 9001, Level = LogLevel.Information, Message = "Business rule rejected the request: {Code}")]
    private partial void LogDomainRule(string code);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Error, Message = "Unhandled exception")]
    private partial void LogUnhandled(Exception exception);
}
