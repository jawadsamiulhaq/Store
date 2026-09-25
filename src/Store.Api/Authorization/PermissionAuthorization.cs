using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Store.Domain.Identity;
using Store.Infrastructure.Identity;

namespace Store.Api.Authorization;

/// <summary>
/// Requires the caller to hold <b>at least one</b> of <see cref="Permissions"/>.
/// </summary>
/// <remarks>
/// A single requirement type covers both "needs this permission" and "needs any of these",
/// because the single case is just a set of one. Keeping it to one type means there is exactly
/// one place where authorisation is decided.
/// </remarks>
public sealed class PermissionRequirement(IReadOnlyList<string> permissions) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}

/// <summary>
/// Evaluates a <see cref="PermissionRequirement"/> against the caller's effective permissions.
/// </summary>
/// <remarks>
/// The check reads from <see cref="IPermissionService"/>, not from token claims. That means a
/// permission revoked in the admin UI stops working on the caller's very next request, instead of
/// lingering until their access token expires. The lookup is cache-backed, so it costs a
/// dictionary hit rather than a query on the hot path.
/// </remarks>
public sealed class PermissionAuthorizationHandler(IPermissionService permissionService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        // The System role is unrestricted by definition, so it short-circuits before any lookup.
        if (context.User.IsInRole(RoleNames.System))
        {
            context.Succeed(requirement);
            return;
        }

        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        var effective = await permissionService.GetEffectivePermissionsAsync(userId);

        if (requirement.Permissions.Any(effective.Contains))
        {
            context.Succeed(requirement);
        }

        // Not calling Fail(): leaving the requirement unmet lets other handlers (if any are ever
        // added) still satisfy it. Unmet requirements produce a 403 regardless.
    }
}

/// <summary>
/// Creates a permission policy on demand, so endpoints can require any permission without every
/// one of them being registered by hand at startup.
/// </summary>
/// <remarks>
/// A policy name is the <c>perm:</c> prefix followed by one or more comma-separated permission
/// codes. Endpoints never build these names by hand — they pass
/// <see cref="Store.Domain.Identity.Permissions"/> constants to the extension methods below, so a
/// typo is a compile error rather than a silently unreachable (or silently open) endpoint.
/// </remarks>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private const string Prefix = "perm:";

    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var permissions = policyName[Prefix.Length..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissions))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public static string PolicyName(params string[] permissions) => Prefix + string.Join(',', permissions);
}

public static class AuthorizationExtensions
{
    /// <summary>
    /// Requires a permission on an endpoint or group:
    /// <c>group.MapPost("/", Create).RequirePermission(Permissions.Products.Create);</c>
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(PermissionPolicyProvider.PolicyName(permission));
        return builder;
    }

    /// <summary>
    /// Requires any one of several permissions — for a screen with more than one valid entry
    /// point, such as a dashboard tile visible to holders of either sales or inventory reporting.
    /// </summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentOutOfRangeException.ThrowIfZero(permissions.Length);

        builder.RequireAuthorization(PermissionPolicyProvider.PolicyName(permissions));
        return builder;
    }

    /// <summary>
    /// Requires membership of a role, without any permission check. Use sparingly — only where
    /// the gate genuinely is the role itself (reaching the admin area at all), never as a
    /// shorthand for a capability.
    /// </summary>
    public static TBuilder RequireStaff<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(RoleNames.Staff));

        return builder;
    }
}
