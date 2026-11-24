using System.Text.Json.Serialization;

namespace FinMy.Api.Endpoints.OAuth;

/// <summary>Corpo de <c>POST /oauth/register</c> (RFC 7591), em <c>snake_case</c> como o protocolo.</summary>
public sealed record ClientRegistrationBody(
    [property: JsonPropertyName("client_name")] string? ClientName,
    [property: JsonPropertyName("redirect_uris")] string[]? RedirectUris,
    [property: JsonPropertyName("grant_types")] string[]? GrantTypes,
    [property: JsonPropertyName("response_types")] string[]? ResponseTypes,
    [property: JsonPropertyName("token_endpoint_auth_method")] string? TokenEndpointAuthMethod);

/// <summary>Resposta de <c>POST /oauth/register</c>.</summary>
public sealed record ClientRegistrationResponse(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_id_issued_at")] long ClientIdIssuedAt,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string> RedirectUris,
    [property: JsonPropertyName("grant_types")] string[] GrantTypes,
    [property: JsonPropertyName("response_types")] string[] ResponseTypes,
    [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod);

/// <summary>Erro do protocolo OAuth (RFC 6749 §5.2 / RFC 7591 §3.2.2).</summary>
public sealed record OAuthErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);

/// <summary>Metadados do recurso protegido (RFC 9728).</summary>
public sealed record ProtectedResourceMetadata(
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("authorization_servers")] string[] AuthorizationServers,
    [property: JsonPropertyName("bearer_methods_supported")] string[] BearerMethodsSupported,
    [property: JsonPropertyName("resource_name")] string ResourceName);

/// <summary>Corpo de <c>POST /api/v1/oauth/consent-tickets</c>: os parâmetros do pedido de autorização que a pessoa aprovou.</summary>
public sealed record ConsentTicketRequest(string? ClientId, string? RedirectUri, string? CodeChallenge);

/// <summary>Resposta de <c>POST /api/v1/oauth/consent-tickets</c>.</summary>
public sealed record ConsentTicketResponse(string Ticket, DateTimeOffset ExpiresAt);
