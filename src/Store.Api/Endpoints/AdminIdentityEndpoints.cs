using Microsoft.AspNetCore.Mvc;
using Store.Api.Authorization;
using Store.Api.Extensions;
using Store.Application.Identity;
using Store.Domain.Identity;
using Store.Infrastructure.Identity;

namespace Store.Api.Endpoints;

/// <summary>
/// User, role and permission administration.
/// </summary>
/// <remarks>
/// Every endpoint is gated by a <see cref="Permissions"/> constant rather than a role, so
/// capabilities can be reassigned without touching code. Deeper rules — who may grant the System
/// role, who may widen a role's permissions, whether this is the last System account — live in the
/// services, because they depend on data the authorisation layer cannot see.
/// </remarks>
public static class AdminIdentityEndpoints
{
    public static IEndpointRouteBuilder MapAdminIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        MapUsers(app);
        MapRoles(app);
        MapPermissions(app);

        return app;
    }

    private static void MapUsers(IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/admin/users").WithTags("Admin · Users");

        // Query parameters are declared explicitly rather than via [AsParameters] on the DTO.
        // The binder treats a complex type with inherited init-only properties as a body
        // parameter, which makes a GET fail with 400; being explicit also produces accurate
        // OpenAPI documentation for each filter.
        users.MapGet("/", async (
                    IUserAdminService service,
                    CancellationToken ct,
                    string? search = null,
                    string? role = null,
                    bool? isActive = null,
                    string? sortBy = null,
                    bool descending = false,
                    int page = 1,
                    int pageSize = 24) =>
                Results.Ok(await service.ListAsync(
                    new UserQuery
                    {
                        Search = search,
                        Role = role,
                        IsActive = isActive,
                        SortBy = sortBy,
                        Descending = descending,
                        Page = page,
                        PageSize = pageSize
                    }, ct)))
            .RequirePermission(Permissions.Users.View)
            .WithSummary("List users, paged and filterable.");

        users.MapGet("/{id:guid}", async (Guid id, IUserAdminService service, CancellationToken ct) =>
                (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Users.View)
            .WithSummary("A user with their roles, overrides and effective permissions.");

        users.MapPost("/", async ([FromBody] CreateUserRequest request, IUserAdminService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreatedResult(u => $"/api/admin/users/{u.Id}"))
            .RequirePermission(Permissions.Users.Create)
            .WithSummary("Create a user account.");

        users.MapPut("/{id:guid}", async (Guid id, [FromBody] UpdateUserRequest request, IUserAdminService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Users.Update)
            .WithSummary("Update a user's profile, status and roles.");

        users.MapDelete("/{id:guid}", async (Guid id, IUserAdminService service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Users.Delete)
            .WithSummary("Deactivate a user and revoke their sessions.");

        users.MapPut("/{id:guid}/permissions", async (Guid id, [FromBody] SetUserPermissionsRequest request, IUserAdminService service, CancellationToken ct) =>
                (await service.SetPermissionOverridesAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Users.ManagePermissions)
            .WithSummary("Replace a user's per-user permission grants and denials.");
    }

    private static void MapRoles(IEndpointRouteBuilder app)
    {
        var roles = app.MapGroup("/api/admin/roles").WithTags("Admin · Roles");

        roles.MapGet("/", async (IRoleAdminService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .RequirePermission(Permissions.Roles.View)
            .WithSummary("All roles with their member counts and permissions.");

        roles.MapGet("/{id:guid}", async (Guid id, IRoleAdminService service, CancellationToken ct) =>
                (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Roles.View);

        roles.MapPost("/", async ([FromBody] CreateRoleRequest request, IRoleAdminService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreatedResult(r => $"/api/admin/roles/{r.Id}"))
            .RequirePermission(Permissions.Roles.Create)
            .WithSummary("Create a custom role.");

        roles.MapPut("/{id:guid}", async (Guid id, [FromBody] UpdateRoleRequest request, IRoleAdminService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Update);

        roles.MapDelete("/{id:guid}", async (Guid id, IRoleAdminService service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Delete);

        roles.MapPut("/{id:guid}/permissions", async (Guid id, [FromBody] SetRolePermissionsRequest request, IRoleAdminService service, CancellationToken ct) =>
                (await service.SetPermissionsAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Roles.AssignPermissions)
            .WithSummary("Replace a role's permission set.");
    }

    private static void MapPermissions(IEndpointRouteBuilder app)
    {
        var permissions = app.MapGroup("/api/admin/permissions").WithTags("Admin · Permissions");

        permissions.MapGet("/", async (IRoleAdminService service, CancellationToken ct) =>
                Results.Ok(await service.GetPermissionCatalogueAsync(ct)))
            // Readable by anyone who can administer either roles or users, since both screens
            // render the same matrix.
            .RequireAnyPermission(Permissions.Roles.View, Permissions.Users.View)
            .WithSummary("The permission catalogue, grouped by module.");
    }
}
