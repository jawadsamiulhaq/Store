using System.Security.Claims;
using Store.Application.Common;

namespace Store.Api.Authorization;

/// <summary>
/// Supplies the ambient request identity to the auditing interceptor.
/// </summary>
/// <remarks>
/// Registered as a <b>singleton</b>, which is safe because it holds no state of its own — every
/// property is read through <see cref="IHttpContextAccessor"/>, itself a singleton backed by an
/// async-local that resolves to the current request.
/// <para>
/// This exists separately from <see cref="CurrentUser"/> for a lifetime reason. The auditing
/// interceptor is built into the pooled <c>DbContext</c>'s options, which EF Core resolves from
/// the root service provider; a scoped dependency there throws at startup. <see cref="CurrentUser"/>
/// cannot be a singleton because it needs the scoped <c>IPermissionService</c> for permission
/// checks — and the interceptor never needs those, so the two concerns are split rather than
/// forced together.
/// </para>
/// </remarks>
public sealed class AuditContext(IHttpContextAccessor accessor) : IAuditContext
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public string? IpAddress
    {
        get
        {
            var context = accessor.HttpContext;

            if (context is null)
            {
                return null;
            }

            // Behind nginx or a CDN the socket address is the proxy, not the client.
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

    public string? UserAgent =>
        accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}
