namespace Store.Application.Identity;

// ---- Requests -----------------------------------------------------------------------------

public sealed record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? Phone,
    bool AcceptsMarketing);

public sealed record LoginRequest(string Email, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string? Phone,
    string? AvatarUrl,
    string? PreferredLanguage);

// ---- Responses ----------------------------------------------------------------------------

/// <summary>
/// Issued on login and refresh. The refresh token is <b>not</b> in this payload — it is set as an
/// HttpOnly, Secure, SameSite cookie, so script running on the page cannot read or exfiltrate it.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    CurrentUserDto User);

/// <summary>
/// The caller's identity and effective permissions.
/// </summary>
/// <remarks>
/// Permissions are delivered here rather than as JWT claims. Putting them in the token would
/// bloat every request, and — more importantly — would leave a revoked permission live until the
/// token expired. Here the frontend refetches on demand and the server re-resolves per request.
/// </remarks>
public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? Phone,
    string? AvatarUrl,
    string PreferredLanguage,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool IsSystem,
    bool IsStaff);

// ---- User administration ------------------------------------------------------------------

public sealed record UserListItemDto(
    Guid Id,
    string Email,
    string FullName,
    string? Phone,
    string? AvatarUrl,
    bool IsActive,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record UserDetailDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    string? AvatarUrl,
    bool IsActive,
    bool EmailConfirmed,
    string PreferredLanguage,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> EffectivePermissions,
    IReadOnlyList<UserPermissionOverrideDto> Overrides,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

/// <summary>A per-user grant or deny layered on top of role permissions.</summary>
public sealed record UserPermissionOverrideDto(string PermissionCode, bool IsGranted);

public sealed record CreateUserRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? Phone,
    IReadOnlyList<string> Roles,
    bool IsActive);

public sealed record UpdateUserRequest(
    string FirstName,
    string LastName,
    string? Phone,
    bool IsActive,
    IReadOnlyList<string>? Roles);

public sealed record SetUserPermissionsRequest(IReadOnlyList<UserPermissionOverrideDto> Overrides);

public sealed record UserQuery : Common.PagedQuery
{
    public string? Search { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
    public string? SortBy { get; init; }
    public bool Descending { get; init; }
}

// ---- Roles & permissions --------------------------------------------------------------------

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    int UserCount,
    IReadOnlyList<string> Permissions);

public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record UpdateRoleRequest(string Name, string? Description);

public sealed record SetRolePermissionsRequest(IReadOnlyList<string> Permissions);

/// <summary>One row of the admin permission matrix.</summary>
public sealed record PermissionDto(
    Guid Id,
    string Code,
    string Module,
    string DisplayName,
    string? Description);

/// <summary>Permissions grouped by module, for rendering the matrix.</summary>
public sealed record PermissionModuleDto(string Module, IReadOnlyList<PermissionDto> Permissions);
