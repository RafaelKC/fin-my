using FinMy.Api.Endpoints.Auth;

namespace FinMy.Api.Endpoints.Profile;

/// <summary>Corpo de <c>PATCH /api/v1/me</c> e <c>PATCH /api/v1/tenant</c>.</summary>
public sealed record RenameRequest(string? Name);

/// <summary>Corpo de <c>POST /api/v1/me/password</c>.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>Usuário e Espaço do contexto (<c>MeResponse</c>).</summary>
public sealed record MeResponse(UserDto User, TenantDto Tenant);

/// <summary>Aplicativo conectado (<c>GET /api/v1/me/connected-apps</c>): <c>id</c> é o da autorização, usado em <c>DELETE</c>.</summary>
public sealed record ConnectedAppResponse(string Id, string Name, DateTimeOffset ConnectedAt, DateTimeOffset? LastUsedAt);
