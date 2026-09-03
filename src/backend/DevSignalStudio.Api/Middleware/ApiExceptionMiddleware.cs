using System.Diagnostics;
using System.Text.Json;
using DevSignalStudio.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace DevSignalStudio.Api.Middleware;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected. There is no useful response to write.
        }
        catch (Exception exception)
        {
            await WriteProblemAsync(context, exception);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        (int status, string title, string code) = exception switch
        {
            ResourceNotFoundException resource =>
                (StatusCodes.Status404NotFound, "Resource not found", resource.Code),
            ConcurrencyConflictException concurrency =>
                (StatusCodes.Status409Conflict, "Revision conflict", concurrency.Code),
            RequestValidationException validation =>
                (StatusCodes.Status400BadRequest, "Validation failed", validation.Code),
            DomainRuleException domain =>
                (StatusCodes.Status422UnprocessableEntity, "Business rule rejected the request", domain.Code),
            JsonException =>
                (StatusCodes.Status400BadRequest, "Invalid JSON", "invalid_json"),
            InvalidDataException =>
                (StatusCodes.Status400BadRequest, "Invalid configuration", "invalid_configuration"),
            BadHttpRequestException =>
                (StatusCodes.Status400BadRequest, "Invalid request", "invalid_request"),
            _ =>
                (StatusCodes.Status500InternalServerError, "Unexpected server error", "unexpected_error")
        };

        if (status >= 500)
        {
            _logger.LogError(exception, "Unhandled API exception for {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "API request was rejected for {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);
        }

        string traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        ProblemDetails problem = new()
        {
            Status = status,
            Title = title,
            Detail = status >= 500
                ? "An unexpected error occurred. Use the trace ID to locate the server log entry."
                : exception.Message,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = traceId;
        if (exception is RequestValidationException validationException &&
            validationException.Errors.Count > 0)
        {
            problem.Extensions["errors"] = validationException.Errors;
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("The response had already started; a problem response could not be written.");
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
