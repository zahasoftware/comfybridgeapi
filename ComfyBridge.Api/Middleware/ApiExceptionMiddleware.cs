using ComfyBridge.Domain.Exceptions;

namespace ComfyBridge.Api.Middleware;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception while processing request");
            await WriteErrorResponseAsync(context, ex);
        }
    }

    private static Task WriteErrorResponseAsync(HttpContext context, Exception ex)
    {
        var (status, title) = ex switch
        {
            TemplateNotFoundException => (StatusCodes.Status404NotFound, "Template not found"),
            InputValidationException => (StatusCodes.Status400BadRequest, "Invalid input"),
            TimeoutException => (StatusCodes.Status504GatewayTimeout, "Upstream timeout"),
            ExternalServiceException => (StatusCodes.Status502BadGateway, "Upstream service failure"),
            ComfyBridgeException => (StatusCodes.Status404NotFound, "Resource not found"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "Server error")
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        return context.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title,
            status,
            detail = ex.Message,
            traceId = context.TraceIdentifier
        });
    }
}