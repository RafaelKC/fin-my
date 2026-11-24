using System.Security.Cryptography;
using FinMy.Application.Abstractions;
using FinMy.Application.OAuth;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FinMy.Infrastructure.Auth.OAuth;

/// <summary>
/// Registro dinâmico (RFC 7591, 008 research R2): cria uma aplicação <b>pública</b> (sem segredo, PKCE obrigatório) e só
/// aceita redirects da lista <c>Mcp:AllowedRedirectUris</c>.
/// </summary>
public sealed class ClientRegistrationService(IOpenIddictApplicationManager applications, IOptions<McpOptions> options, IClock clock) : IClientRegistrationService
{
    public const string InvalidRedirectUri = "invalid_redirect_uri";
    public const string InvalidClientMetadata = "invalid_client_metadata";

    private const int NameMaxLength = 100;
    private const int RedirectsMax = 10;

    public async Task<(RegisteredClient? Client, ClientRegistrationError? Error)> RegisterAsync(ClientRegistrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.ClientName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > NameMaxLength)
        {
            return (null, new ClientRegistrationError(InvalidClientMetadata, $"Informe client_name com até {NameMaxLength} caracteres."));
        }

        var redirects = request.RedirectUris;
        if (redirects is null || redirects.Count == 0 || redirects.Count > RedirectsMax)
        {
            return (null, new ClientRegistrationError(InvalidRedirectUri, "Informe de 1 a 10 redirect_uris."));
        }

        var allowed = options.Value.EffectiveAllowedRedirectUris;
        if (redirects.FirstOrDefault(uri => !allowed.Contains(uri, StringComparer.Ordinal)) is { } denied)
        {
            return (null, new ClientRegistrationError(InvalidRedirectUri, $"redirect_uri não permitido: {denied}"));
        }

        if (request.TokenEndpointAuthMethod is not (null or "none"))
        {
            return (null, new ClientRegistrationError(InvalidClientMetadata, "Só clientes públicos são aceitos (token_endpoint_auth_method = none)."));
        }

        if (request.GrantTypes is { } grants && grants.Any(g => g is not (GrantTypes.AuthorizationCode or GrantTypes.RefreshToken)))
        {
            return (null, new ClientRegistrationError(InvalidClientMetadata, "grant_types aceitos: authorization_code e refresh_token."));
        }

        if (request.ResponseTypes is { } responses && responses.Any(r => r != ResponseTypes.Code))
        {
            return (null, new ClientRegistrationError(InvalidClientMetadata, "response_types aceito: code."));
        }

        var clientId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = name,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        foreach (var uri in redirects.Distinct(StringComparer.Ordinal))
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        await applications.CreateAsync(descriptor, cancellationToken);
        return (new RegisteredClient(clientId, name, redirects.Distinct(StringComparer.Ordinal).ToList(), clock.UtcNow), null);
    }
}
