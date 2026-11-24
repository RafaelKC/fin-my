using FinMy.Application.OAuth;

namespace FinMy.Application.Abstractions;

/// <summary>Registro dinâmico de clientes OAuth (RFC 7591): só clientes públicos, só com redirects da lista permitida.</summary>
public interface IClientRegistrationService
{
    Task<(RegisteredClient? Client, ClientRegistrationError? Error)> RegisterAsync(ClientRegistrationRequest request, CancellationToken cancellationToken);
}
