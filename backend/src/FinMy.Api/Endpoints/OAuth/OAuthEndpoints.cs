using System.Collections.Immutable;
using System.Security.Claims;
using FinMy.Api.Composition;
using FinMy.Api.Errors;
using FinMy.Application.Abstractions;
using FinMy.Application.Auth;
using FinMy.Application.OAuth;
using FinMy.Domain.Common;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using OpenIddictErrors = OpenIddict.Abstractions.OpenIddictConstants.Errors;

namespace FinMy.Api.Endpoints.OAuth;

/// <summary>
/// OAuth 2.1 do conector do Claude (008 research R2): descoberta, registro dinâmico, <c>/oauth/authorize</c> (passthrough:
/// leva ao front ou conclui com o ticket de consentimento), <c>/oauth/token</c> e <c>POST /api/v1/oauth/consent-tickets</c>.
/// O OpenIddict valida o pedido (cliente, redirect, PKCE S256) antes de chegar aqui.
/// </summary>
public sealed class OAuthEndpoints : IEndpointModule
{
    public const string ConsentTicketParameter = "consent_ticket";
    public const string ConsentDeniedParameter = "consent_denied";
    public const string FrontAuthorizePath = "/autorizar";
    private const string TenantClaim = "tid";
    private const string ChannelClaim = "channel";
    public const string ChannelClaude = "claude";

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Protocolo OAuth: fora do contrato OpenAPI do app.
        app.MapGet("/.well-known/oauth-protected-resource", ProtectedResource).ExcludeFromDescription();
        app.MapGet("/.well-known/oauth-protected-resource/mcp", ProtectedResource).ExcludeFromDescription();
        app.MapPost(OAuthSetup.RegisterUri, (Delegate)RegisterAsync).RequireRateLimiting(RateLimitPolicies.Auth).ExcludeFromDescription();
        app.MapMethods(OAuthSetup.AuthorizeUri, [HttpMethods.Get, HttpMethods.Post], (Delegate)AuthorizeAsync).ExcludeFromDescription();
        app.MapPost(OAuthSetup.TokenUri, (Delegate)ExchangeAsync).ExcludeFromDescription();

        app.MapPost("/api/v1/oauth/consent-tickets", (Delegate)CreateConsentTicketAsync)
            .WithTags("oauth")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Auth);
    }

    private static Ok<ProtectedResourceMetadata> ProtectedResource(IConfiguration configuration)
    {
        var appUrl = OAuthSetup.ResolveAppUrl(configuration);
        return TypedResults.Ok(new ProtectedResourceMetadata(OAuthSetup.ResourceUrl(appUrl), [appUrl], ["header"], "Fin-My"));
    }

    private static async Task<IResult> RegisterAsync(ClientRegistrationBody body, IClientRegistrationService registration, CancellationToken cancellationToken)
    {
        var (client, error) = await registration.RegisterAsync(
            new ClientRegistrationRequest(body.ClientName, body.RedirectUris, body.GrantTypes, body.ResponseTypes, body.TokenEndpointAuthMethod),
            cancellationToken);

        if (client is null)
        {
            return TypedResults.Json(new OAuthErrorResponse(error!.Error, error.Description), statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.Json(
            new ClientRegistrationResponse(
                client.ClientId,
                client.IssuedAt.ToUnixTimeSeconds(),
                client.ClientName,
                client.RedirectUris,
                [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken],
                [ResponseTypes.Code],
                "none"),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext http,
        IConsentTicketService tickets,
        IOpenIddictApplicationManager applications,
        IOpenIddictAuthorizationManager authorizations,
        AppUrl appUrl,
        CancellationToken cancellationToken)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("O pedido de autorização do OpenIddict não está disponível.");

        // "Cancelar" na tela de consentimento: o front volta aqui com os mesmos parâmetros; como o OpenIddict já validou
        // cliente e redirect, a negação só pode ir para um endereço registrado.
        if (request[ConsentDeniedParameter] is { } denied && !string.IsNullOrEmpty(denied.ToString()))
        {
            var query = new List<KeyValuePair<string, string?>> { new("error", OpenIddictErrors.AccessDenied), new("error_description", "A pessoa não autorizou o acesso.") };
            if (!string.IsNullOrEmpty(request.State))
            {
                query.Add(new("state", request.State));
            }

            return TypedResults.Redirect(QueryHelpers.AddQueryString(request.RedirectUri!, query));
        }

        // Sem ticket: a pessoa ainda precisa entrar e consentir, no front, com os mesmos parâmetros.
        if (request[ConsentTicketParameter]?.ToString() is not { Length: > 0 } ticket)
        {
            var parameters = (http.Request.HasFormContentType ? http.Request.Form : (IEnumerable<KeyValuePair<string, StringValues>>)http.Request.Query)
                .Where(p => p.Key is not (ConsentTicketParameter or ConsentDeniedParameter))
                .SelectMany(p => p.Value.Select(v => KeyValuePair.Create(p.Key, v)))
                .ToList();
            return TypedResults.Redirect(QueryHelpers.AddQueryString(appUrl.Combine(FrontAuthorizePath), parameters!));
        }

        var consent = await tickets.RedeemAsync(ticket, request.ClientId, request.RedirectUri, request.CodeChallenge, cancellationToken);
        if (consent is null)
        {
            return Forbid(OpenIddictErrors.AccessDenied, "O consentimento expirou ou não vale para este pedido. Autorize novamente.");
        }

        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("Aplicativo validado pelo OpenIddict não encontrado.");

        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, consent.UserId.ToString());
        identity.SetClaim(Claims.Name, consent.UserName);
        identity.SetClaim(TenantClaim, consent.TenantId.ToString());
        identity.SetClaim(ChannelClaim, ChannelClaude);
        identity.SetDestinations(_ => [Destinations.AccessToken]);

        var principal = new ClaimsPrincipal(identity);
        var scopes = request.GetScopes().Add(Scopes.OfflineAccess).Distinct().ToImmutableArray();
        principal.SetScopes(scopes);
        principal.SetResources(OAuthSetup.ResourceUrl(appUrl.BaseUrl.TrimEnd('/')));

        // Autorização permanente: é ela que "Aplicativos conectados" lista e revoga (T002) e que a validação confere a cada chamada.
        var authorization = await authorizations.CreateAsync(
            principal,
            consent.UserId.ToString(),
            await applications.GetIdAsync(application, cancellationToken) ?? throw new InvalidOperationException("Aplicativo sem id."),
            AuthorizationTypes.Permanent,
            scopes,
            cancellationToken);
        principal.SetAuthorizationId(await authorizations.GetIdAsync(authorization, cancellationToken));

        return TypedResults.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangeAsync(HttpContext http)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("O pedido de token do OpenIddict não está disponível.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Forbid(OpenIddictErrors.UnsupportedGrantType, "Só authorization_code e refresh_token são aceitos.");
        }

        // O código ou o refresh token já foi validado (e, no refresh, rotacionado) pelo OpenIddict; as claims seguem como emitidas.
        var result = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        return result.Principal is { } principal
            ? TypedResults.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
            : Forbid(OpenIddictErrors.InvalidGrant, "A autorização não é mais válida.");
    }

    private static async Task<IResult> CreateConsentTicketAsync(
        ConsentTicketRequest body,
        ICurrentContext context,
        IConsentTicketService tickets,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var userId = context.UserId ?? throw new InvalidOperationException("Endpoint autenticado sem usuário.");
        var (issued, rejection) = await tickets.IssueAsync(userId, context.TenantIdOrEmpty, body.ClientId, body.RedirectUri, body.CodeChallenge, cancellationToken);
        if (issued is null)
        {
            return ProblemResults.From(rejection switch
            {
                ConsentTicketRejection.UnknownClient => new DomainError("oauth_client_unknown", "Aplicativo desconhecido. Adicione o conector de novo no Claude.", DomainErrorKind.Validation),
                ConsentTicketRejection.RedirectNotRegistered => new DomainError("oauth_redirect_not_registered", "O endereço de retorno não pertence a este aplicativo.", DomainErrorKind.Validation),
                _ => new DomainError("oauth_code_challenge_invalid", "Pedido de autorização inválido.", DomainErrorKind.Validation),
            });
        }

        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Json(new ConsentTicketResponse(issued.Ticket, issued.ExpiresAt), statusCode: StatusCodes.Status201Created);
    }

    private static ForbidHttpResult Forbid(string error, string description)
        => TypedResults.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
