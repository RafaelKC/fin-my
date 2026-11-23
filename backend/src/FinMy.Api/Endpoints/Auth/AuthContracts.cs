namespace FinMy.Api.Endpoints.Auth;

/// <summary>Corpo de <c>POST /api/v1/auth/signup</c> (contracts/openapi.yaml <c>SignUpRequest</c>).</summary>
public sealed record SignUpRequest(string? Name, string? Email, string? Password);

/// <summary>Corpo de <c>POST /api/v1/auth/login</c> (<c>LoginRequest</c>).</summary>
public sealed record LoginRequest(string? Email, string? Password);

public sealed record UserDto(Guid Id, string Name, string Email);

public sealed record TenantDto(Guid Id, string Name, string Role);

/// <summary>Resposta de cadastro/login/refresh (<c>SessionResponse</c>). O refresh token vai só no cookie.</summary>
public sealed record SessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, UserDto User, TenantDto Tenant);
