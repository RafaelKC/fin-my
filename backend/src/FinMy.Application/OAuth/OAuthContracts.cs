namespace FinMy.Application.OAuth;

/// <summary>Ticket emitido para o front (a parte opaca e a validade).</summary>
public sealed record IssuedConsentTicket(string Ticket, DateTimeOffset ExpiresAt);

/// <summary>Pessoa e Espaço que autorizaram o pedido do ticket consumido.</summary>
public sealed record RedeemedConsent(Guid UserId, Guid TenantId, string UserName);

/// <summary>Por que um pedido de ticket foi recusado.</summary>
public enum ConsentTicketRejection
{
    None,
    UnknownClient,
    RedirectNotRegistered,
    InvalidChallenge,
}

/// <summary>Pedido de registro dinâmico de cliente (RFC 7591), já lido do JSON.</summary>
public sealed record ClientRegistrationRequest(
    string? ClientName,
    IReadOnlyList<string>? RedirectUris,
    IReadOnlyList<string>? GrantTypes,
    IReadOnlyList<string>? ResponseTypes,
    string? TokenEndpointAuthMethod);

/// <summary>Cliente criado.</summary>
public sealed record RegisteredClient(string ClientId, string ClientName, IReadOnlyList<string> RedirectUris, DateTimeOffset IssuedAt);

/// <summary>Erro do RFC 7591 (<c>invalid_redirect_uri</c> ou <c>invalid_client_metadata</c>).</summary>
public sealed record ClientRegistrationError(string Error, string Description);
