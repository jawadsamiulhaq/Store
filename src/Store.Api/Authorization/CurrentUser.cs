using System.Security.Claims;
using Store.Application.Common;
using Store.Domain.Identity;
using Store.Infrastructure.Identity;

namespace Store.Api.Authorization;

/// <summary>
/// <see cref="ICurrentUser"/> backed by the ambient <see cref="HttpContext"/>.
/// </summary>
/// <remarks>
/// This lives in the API layer, not in Infrastructure, because it is the one place where an HTTP
/// concept (the request, its cookies, its headers) is translated into the plain abstraction the
/// rest of the application consumes. Services stay testable with a hand-written stub.
/// <para>
/// Every property tolerates there being no request at all — background jobs, the startup seeder
/// and design-time tooling all resolve services outside a request scope, and an audit stamp is
/// not worth a <see cref="NullReferenceException"/>.
/// </para>
/// </remarks>
public sealed class CurrentUser(
    IHttpContextAccessor accessor,
    IPermissionService permissionService) : ICurrentUser
{
    /// <summary>Cookie holding a guest's cart identity before they sign in.</summary>
    public const string AnonymousIdCookie = "wps_aid";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsSystem => Principal?.IsInRole(RoleNames.System) == true;

    public bool IsStaff => IsSystem || Principal?.IsInRole(RoleNames.Admin) == true;

    public string? AnonymousId => accessor.HttpContext?.Request.Cookies[AnonymousIdCookie];

    public string? IpAddress
    {
        get
        {
            var context = accessor.HttpContext;

            if (context is null)
            {
                return null;
            }

            // Behind nginx or a CDN the socket address is the proxy, not the client. Forwarded
            // headers middleware normally rewrites RemoteIpAddress, but only for proxies on the
            // known-networks allowlist; this fallback keeps audit records useful when it does not.
            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
            {
                var first = forwarded.ToString().Split(',', StringSplitOptions.TrimEntries).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(first))
                {
                    return first;
                }
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
        ? ua
        : null;

    /// <summary>
    /// Ties an audit row back to the request that produced it, and to the structured logs.
    /// Set by the correlation-id middleware, so this is a read of an already-resolved value.
    /// </summary>
    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;

    public async Task<bool> HasPermissionAsync(string permission, CancellationToken ct = default)
    {
        if (IsSystem)
        {
            return true;
        }

        return UserId is { } userId
            && await permissionService.HasPermissionAsync(userId, permission, ct);
    }
}
