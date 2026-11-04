using System.Net;
using System.Text.Json;
using PatientManagement.Core.DTOs.Common;

namespace PatientManagement.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred during request execution: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message, errors) = exception switch
        {
            KeyNotFoundException notFound => (
                HttpStatusCode.NotFound,
                notFound.Message,
                new List<string> { notFound.Message }),

            ArgumentException argEx => (
                HttpStatusCode.BadRequest,
                argEx.Message,
                new List<string> { argEx.Message }),

            InvalidOperationException opEx => (
                HttpStatusCode.BadRequest,
                opEx.Message,
                new List<string> { opEx.Message }),

            _ => (
                HttpStatusCode.InternalServerError,
                "An unexpected internal error occurred on the server.",
                new List<string> { exception.Message })
        };

        context.Response.StatusCode = (int)statusCode;

        var response = ApiResponse<object>.Fail(message, errors);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        await context.Response.WriteAsync(json);
    }
}
