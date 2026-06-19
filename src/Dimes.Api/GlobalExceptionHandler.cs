using Dimes.Domain.Lifecycle;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api;

/// <summary>Maps application and domain-lifecycle exceptions to RFC 7807 ProblemDetails responses.
/// Lifecycle guard failures surface as 409 (illegal transition) and 403 (insufficient role).</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            BadRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
            InsufficientRoleException => (StatusCodes.Status403Forbidden, "Insufficient role"),
            InvalidTransitionException => (StatusCodes.Status409Conflict, "Invalid lifecycle transition"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status == StatusCodes.Status500InternalServerError ? null : exception.Message,
            },
        });
    }
}
