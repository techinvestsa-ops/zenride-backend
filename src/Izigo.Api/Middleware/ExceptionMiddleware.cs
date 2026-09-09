using System.Net;
using System.Text.Json;
using Izigo.Application.Common.Models;

namespace Izigo.Api.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (status, code, message) = exception switch
        {
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "UNAUTHORIZED", exception.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, "NOT_FOUND", exception.Message),
            InvalidOperationException e when e.Message.StartsWith("CONFLICT:") =>
                (HttpStatusCode.Conflict, "CONFLICT", e.Message[9..]),
            ArgumentException e => (HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR", e.Message),
            _ => (HttpStatusCode.InternalServerError, "SERVER_ERROR", "An unexpected error occurred.")
        };

        var response = ApiResponse.Fail(code, message);
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)status;
        return context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }
}
