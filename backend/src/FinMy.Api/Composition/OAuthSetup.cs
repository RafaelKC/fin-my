using System.Security.Cryptography;
using System.Text;
using FinMy.Application.Auth;
using FinMy.Infrastructure.Auth;
using FinMy.Infrastructure.Auth.OAuth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FinMy.Api.Composition;

/// <summary>
/// OAuth 2.1 do conector do Claude (008 research R2): servidor e validação do OpenIddict. Código de autorização + PKCE S256,
/// clientes públicos, tokens de referência (consultados no banco a cada chamada: revogar vale na hora), refresh com rotação.
/// Os endpoints <c>/oauth/authorize</c> e <c>/oauth/token</c> são do app (passthrough, ver <c>OAuthEndpoints</c>).
/// </summary>
public static class OAuthSetup
{
    public const string AuthorizeUri = "/oauth/authorize";
    public const string TokenUri = "/oauth/token";
    public const string RegisterUri = "/oauth/register";

    private static readonly string[] PublicClientAuthMethods = ["none"];

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(90);

    /// <summary>Ligadas ao <c>AddOpenIddict()</c> já iniciado pela Infrastructure (núcleo + EF Core).</summary>
    public static IServiceCollection AddFinMyOAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<McpOptions>().Bind(configuration.GetSection(McpOptions.SectionName));

        services.AddOpenIddict()
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris(AuthorizeUri)
                    .SetTokenEndpointUris(TokenUri)
                    .SetConfigurationEndpointUris("/.well-known/oauth-authorization-server", "/.well-known/openid-configuration")
                    .SetJsonWebKeySetEndpointUris("/.well-known/jwks");

                server.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
                server.RequireProofKeyForCodeExchange();

                // Escopos não têm semântica aqui (o conector sempre tem o mesmo acesso): aceita o que o cliente pedir.
                server.DisableScopeValidation().IgnoreScopePermissions();
                server.DisableResourceValidation().IgnoreResourcePermissions();

                server.UseReferenceAccessTokens().UseReferenceRefreshTokens();
                server.SetAccessTokenLifetime(AccessTokenLifetime).SetRefreshTokenLifetime(RefreshTokenLifetime);

                // PKCE somente com S256 (OAuth 2.1).
                server.Configure(options =>
                {
                    options.CodeChallengeMethods.Clear();
                    options.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                });

                server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();

                // Descoberta: inclui `registration_endpoint` (RFC 7591) e o método `none` dos clientes públicos.
                server.AddEventHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>(handler => handler.UseInlineHandler(context =>
                {
                    // O Uri normaliza a raiz com barra final; o RFC 8414 pede o issuer exatamente como o cliente o deriva (sem ela).
                    context.Metadata["issuer"] = context.Options.Issuer!.AbsoluteUri.TrimEnd('/');
                    context.Metadata["registration_endpoint"] = new Uri(context.Options.Issuer!, RegisterUri).AbsoluteUri;
                    context.Metadata["token_endpoint_auth_methods_supported"] = new OpenIddictParameter(System.Text.Json.JsonSerializer.SerializeToElement(PublicClientAuthMethods));
                    return default;
                }));

                // RFC 9207: o `iss` do redirect do authorize precisa ser igual ao `issuer` da descoberta (sem barra final).
                server.AddEventHandler<OpenIddictServerEvents.ApplyAuthorizationResponseContext>(handler => handler
                    .UseInlineHandler(context =>
                    {
                        if (context.Response.Iss is { Length: > 0 } iss)
                        {
                            context.Response.Iss = iss.TrimEnd('/');
                        }

                        return default;
                    })
                    .SetOrder(OpenIddictServerHandlers.Authentication.AttachIssuer.Descriptor.Order + 1));
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.UseAspNetCore();
                validation.EnableAuthorizationEntryValidation().EnableTokenEntryValidation();
            });

        // Valores que dependem da configuração final (chaves e URL pública) são lidos só quando o OpenIddict os usa.
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, ConfigureOpenIddictServer>();
        services.AddSingleton<IConfigureOptions<OpenIddictServerAspNetCoreOptions>, ConfigureOpenIddictServerAspNetCore>();

        return services;
    }

    /// <summary>URL pública do app (<c>APP_URL</c>), sem barra final.</summary>
    public static string ResolveAppUrl(IConfiguration configuration)
        => (configuration["APP_URL"] is { Length: > 0 } appUrl ? appUrl : "http://localhost:8080").TrimEnd('/');

    /// <summary>Recurso protegido: o servidor MCP (<c>/mcp</c>).</summary>
    public static string ResourceUrl(string appUrl) => $"{appUrl}/mcp";

    // Chaves simétricas derivadas da chave do app: estáveis entre reinícios (códigos e tokens de referência continuam válidos),
    // sem um segredo a mais para configurar. O sal separa os usos da chave.
    private static SymmetricSecurityKey DeriveKey(string signingKey, string purpose)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes($"fin-my:oauth:{purpose}:{signingKey}")));

    private sealed class ConfigureOpenIddictServer(IConfiguration configuration, IOptions<JwtOptions> jwt) : IConfigureOptions<OpenIddictServerOptions>
    {
        public void Configure(OpenIddictServerOptions options)
        {
            options.Issuer = new Uri(ResolveAppUrl(configuration), UriKind.Absolute);

            var signingKey = jwt.Value.SigningKey;
            // O OpenIddict exige chave assimétrica: uma P-256 derivada da chave do app (estável entre reinícios, sem arquivo a guardar).
            var ecdsa = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = DeriveKey(signingKey, "signing").Key });
            options.SigningCredentials.Add(new SigningCredentials(new ECDsaSecurityKey(ecdsa) { KeyId = "fin-my-oauth" }, SecurityAlgorithms.EcdsaSha256));
            options.EncryptionCredentials.Add(new EncryptingCredentials(DeriveKey(signingKey, "encryption"), SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512));
        }
    }

    // O TLS termina no proxy à frente do nginx; em desenvolvimento (APP_URL http) o OpenIddict precisa aceitar HTTP.
    private sealed class ConfigureOpenIddictServerAspNetCore(IConfiguration configuration) : IConfigureOptions<OpenIddictServerAspNetCoreOptions>
    {
        public void Configure(OpenIddictServerAspNetCoreOptions options)
            => options.DisableTransportSecurityRequirement = !ResolveAppUrl(configuration).StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
