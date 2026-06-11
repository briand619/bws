using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BubbleSplash.Api.Filters;

public class AdminKeyFilter : IActionFilter
{
    private readonly string? _expectedKey;

    public AdminKeyFilter(IConfiguration configuration)
    {
        _expectedKey = configuration["Admin:ApiKey"];
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (string.IsNullOrWhiteSpace(_expectedKey))
            return; // No key configured — allow through

        context.HttpContext.Request.Headers.TryGetValue("X-Admin-Key", out var provided);

        if (!string.Equals(provided, _expectedKey, StringComparison.Ordinal))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Invalid or missing admin key.",
                Status = StatusCodes.Status401Unauthorized
            })
            { StatusCode = StatusCodes.Status401Unauthorized };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
