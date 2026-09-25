using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Store.Api.Middleware;

public sealed class PerformanceOptions
{
    public const string SectionName = "Performance";

    /// <summary>
    /// Requests slower than this are logged as warnings. Set to the API budget from the
    /// performance targets (&lt;300 ms for normal reads and CRUD), so a regression shows up in the
    /// log rather than in a customer complaint.
    /// </summary>
    public int SlowRequestThresholdMs { get; set; } = 300;

    public int SlowQueryThresholdMs { get; set; } = 150;
}

/// <summary>
/// Assigns a correlation id to every request, echoes it back, and measures how long the request
/// took.
/// </summary>
/// <remarks>
/// The correlation id flows into the log scope, the audit trail and error responses, so one
/// identifier ties a user-visible failure to the exact log entries that explain it.
/// <para>
/// The timing half exists because a performance target nobody measures is a wish. Every response
/// carries <c>Server-Timing</c>, and anything over the threshold is logged with its route — which
/// makes a slow endpoint discoverable without a profiler attached.
/// </para>
/// </remarks>
public sealed class RequestTrackingMiddleware(
    RequestDelegate next,
    IOptions<PerformanceOptions> options,
    ILogger<RequestTrackingMiddleware> logger)
{
    private const string CorrelationHeader = "X-Correlation-Id";

    private readonly int _slowThresholdMs = options.Value.SlowRequestThresholdMs;

    public async Task InvokeAsync(HttpContext context)
    {
        // Honour an incoming id so a trace spans the frontend and the API; otherwise mint one.
        var correlationId = context.Request.Headers.TryGetValue(CorrelationHeader, out var incoming)
                            && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString()
            : Activity.Current?.Id ?? context.TraceIdentifier;

        context.TraceIdentifier = correlationId;

        // Written before the body starts, because headers cannot be added once the response has
        // begun streaming.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeader] = correlationId;
            return Task.CompletedTask;
        });

        var stopwatch = Stopwatch.GetTimestamp();

        using (logger.BeginScope(new Dictionary<string, object>
               {
                   ["CorrelationId"] = correlationId,
                   ["RequestPath"] = context.Request.Path.Value ?? string.Empty
               }))
        {
            try
            {
                await next(context);
            }
            finally
            {
                var elapsed = Stopwatch.GetElapsedTime(stopwatch);
                var ms = elapsed.TotalMilliseconds;

                if (ms > _slowThresholdMs)
                {
                    logger.LogWarning(
                        "Slow request: {Method} {Path} took {ElapsedMs:F0} ms (budget {BudgetMs} ms) → {StatusCode}",
                        context.Request.Method,
                        context.Request.Path,
                        ms,
                        _slowThresholdMs,
                        context.Response.StatusCode);
                }
            }
        }
    }
}

public static class RequestTrackingExtensions
{
    public static IApplicationBuilder UseRequestTracking(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestTrackingMiddleware>();
}
