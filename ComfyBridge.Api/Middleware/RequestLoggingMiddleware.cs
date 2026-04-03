using System.Diagnostics;

namespace ComfyBridge.Api.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var request = context.Request;
        var isWorkflowRoute = request.Path.Value?.StartsWith("/workflows", StringComparison.OrdinalIgnoreCase) == true;

        if (isWorkflowRoute)
        {
            logger.LogInformation("[WORKFLOW] {Method} {Path} from {RemoteIP}",
                request.Method,
                request.Path,
                context.Connection.RemoteIpAddress);

            if (request.Method == "POST" || request.Method == "PUT")
            {
                request.EnableBuffering();
                try
                {
                    using var reader = new StreamReader(request.Body, leaveOpen: true);
                    var body = await reader.ReadToEndAsync();
                    request.Body.Position = 0;

                    if (!string.IsNullOrEmpty(body) && body.Length < 500)
                    {
                        logger.LogDebug("[WORKFLOW] Request body: {Body}", body);
                    }
                    else if (!string.IsNullOrEmpty(body))
                    {
                        logger.LogDebug("[WORKFLOW] Request body size: {Size} bytes", body.Length);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[WORKFLOW] Error reading request body");
                }
            }
        }

        if (!isWorkflowRoute)
        {
            await next(context);
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            if (isWorkflowRoute)
            {
                logger.LogInformation("[WORKFLOW] {Method} {Path} => {StatusCode} ({ElapsedMs}ms)",
                    request.Method,
                    request.Path,
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds);

                if (context.Response.StatusCode >= 400)
                {
                    var buffer = responseBody.ToArray();
                    if (buffer.Length > 0 && buffer.Length < 1000)
                    {
                        try
                        {
                            var responseText = System.Text.Encoding.UTF8.GetString(buffer);
                            logger.LogWarning("[WORKFLOW] Error response: {Response}", responseText);
                        }
                        catch { }
                    }
                }
            }

            responseBody.Position = 0;
            await responseBody.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;
        }
    }
}
