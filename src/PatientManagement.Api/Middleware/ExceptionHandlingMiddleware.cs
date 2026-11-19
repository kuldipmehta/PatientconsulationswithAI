using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.Exceptions;

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
            _logger.LogError(ex, "Request failed with exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted) return;

        context.Response.ContentType = "application/json";

        var (statusCode, message, errors) = exception switch
        {
            ForbiddenAccessException forbidden => (
                HttpStatusCode.Forbidden,
                forbidden.Message,
                new List<string> { forbidden.Message }),

            BookingConflictException conflict => (
                HttpStatusCode.Conflict,
                conflict.Message,
                new List<string> { conflict.Message }),

            DbUpdateConcurrencyException => (
                HttpStatusCode.Conflict,
                "Concurrency conflict: This record was modified by another user. Please refresh and try again.",
                new List<string> { "Stale RowVersion token detected." }),

            EmbeddingUnavailableException => (
                HttpStatusCode.ServiceUnavailable,
                "The document indexing service is temporarily unavailable. Please try again later.",
                new List<string> { "Embedding service unavailable." }),

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
                new List<string> { $"Reference: {context.TraceIdentifier}" })
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
