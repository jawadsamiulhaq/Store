using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Store.Api.Middleware;

/// <summary>
/// Converts any unhandled exception into an RFC 9457 <see cref="ProblemDetails"/> response.
/// </summary>
/// <remarks>
/// Two rules drive this:
/// <list type="bullet">
///   <item>
///     <b>Nothing internal crosses the wire in production.</b> Exception messages and stack traces
///     routinely contain table names, file paths and connection details. Outside development the
///     client gets a generic message and a correlation id; the detail goes to the log only.
///   </item>
///   <item>
///     <b>Every response carries a correlation id.</b> A user can quote it in a support message
///     and it resolves directly to the logged exception.
///   </item>
/// </list>
/// </remarks>
public sealed class GlobalExceptionHandler(
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = context.TraceIdentifier;

        // A cancelled request is the client hanging up, not a fault. Logging these as errors
        // fills the log with noise every time someone navigates away mid-request.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {Path} was cancelled by the client", context.Request.Path);
            return true;
        }

        var (status, title, detail) = Map(exception, environment.IsDevelopment());

        // A concurrency failure says only "0 rows affected" by default, which is close to
        // undiagnosable. Naming the entities EF thought were in conflict turns it into a
        // one-line diagnosis.
        if (exception is DbUpdateConcurrencyException concurrency)
        {
            foreach (var entry in concurrency.Entries)
            {
                logger.LogError(
                    "Concurrency conflict on {EntityType} (state {State}), key {Key}",
                    entry.Entity.GetType().Name,
                    entry.State,
                    entry.Metadata.FindPrimaryKey()?.Properties
                        .Select(p => $"{p.Name}={entry.Property(p.Name).CurrentValue}")
                        .Aggregate((a, b) => $"{a}, {b}") ?? "unknown");
            }
        }

        // A client-side fault (bad JSON, a stale row) is not an incident. Logging 4xx at Error
        // level would drown genuine server faults in noise and make the error log useless as an
        // alerting signal.
        logger.Log(
            status >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning,
            exception,
            "{ExceptionType} on {Method} {Path} → {Status} (correlation {CorrelationId})",
            exception.GetType().Name,
            context.Request.Method,
            context.Request.Path,
            status,
            correlationId);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions = { ["correlationId"] = correlationId }
        };

        if (environment.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.GetType().FullName;
            problem.Extensions["stackTrace"] = exception.StackTrace;
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    private static (int Status, string Title, string Detail) Map(Exception exception, bool isDevelopment) =>
        exception switch
        {
            // A body that will not parse is the caller's mistake, not a server fault. Without this
            // arm it falls through to 500, which tells the client to retry something that can
            // never succeed — and hides a genuine integration bug behind an alarming status.
            BadHttpRequestException bad => (
                bad.StatusCode is StatusCodes.Status400BadRequest or StatusCodes.Status413PayloadTooLarge
                    ? bad.StatusCode
                    : StatusCodes.Status400BadRequest,
                "Invalid request",
                "The request body could not be read. Check that it is well-formed JSON and matches the expected shape."),

            System.Text.Json.JsonException => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                "The request body is not valid JSON."),

            // Two writers touched the same row. The caller's copy is stale, so the correct
            // recovery is to reload and retry — which is a 409, not a 500.
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Conflict",
                "This record was changed by someone else while you were editing it. Please reload and try again."),

            DbUpdateException when IsUniqueViolation(exception) => (
                StatusCodes.Status409Conflict,
                "Conflict",
                "A record with these details already exists."),

            UnauthorizedAccessException => (
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "You do not have permission to perform this action."),

            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "Not found",
                "The requested resource does not exist."),

            ArgumentException or InvalidOperationException when isDevelopment => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                exception.Message),

            TimeoutException => (
                StatusCodes.Status504GatewayTimeout,
                "Timeout",
                "The request took too long to complete. Please try again."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Server error",
                isDevelopment
                    ? exception.Message
                    : "Something went wrong on our side. Please try again, or quote the correlation id if it persists.")
        };

    /// <summary>
    /// Detects a SQL Server unique-constraint violation so a duplicate slug or SKU surfaces as a
    /// 409 with a usable message rather than an opaque 500.
    /// </summary>
    private static bool IsUniqueViolation(Exception exception) =>
        exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
}
