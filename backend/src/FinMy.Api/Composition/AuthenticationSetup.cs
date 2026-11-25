using FinMy.Api.Errors;
using FinMy.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FinMy.Api.Composition;

/// <summary>Configuração do <c>JwtBearer</c> e dos erros 401 (<c>token_expired</c> quando o access token venceu).</summary>
public static class AuthenticationSetup
{
    public const string SelectorScheme = "FinMy";
    public const string McpPath = "/mcp";
    public const string WidgetPath = "/api/v1/widget";

    /// <summary>Políticas que exigem o recurso (audiência) do token do OpenIddict: cada grupo de rotas aceita só o seu (FR-004).</summary>
    public const string McpResourcePolicy = "McpResource";
    public const string WidgetResourcePolicy = "WidgetResource";
    public const string TokenExpiredCode = "token_expired";

    public static IServiceCollection AddFinMyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateOnStart();

        // O app usa o JWT próprio; o conector do Claude (/mcp, 008) e o widget Android (/api/v1/widget, 009) usam os tokens do
        // OpenIddict. Cada rota só aceita um deles: um token do OpenIddict não alcança a API REST do app e um JWT do app não
        // alcança /mcp nem o widget. Entre os dois grupos do OpenIddict, o recurso do token decide (políticas abaixo).
        services.AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, displayName: null, options => options.ForwardDefaultSelector = context =>
                UsesOpenIddict(context.Request.Path)
                    ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                    : JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddFinMyOAuth(configuration);
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(McpResourcePolicy, policy => policy.RequireAuthenticatedUser().AddRequirements(new ResourceRequirement(OAuthSetup.ResourceUrl)));
            options.AddPolicy(WidgetResourcePolicy, policy => policy.RequireAuthenticatedUser().AddRequirements(new ResourceRequirement(OAuthSetup.WidgetResourceUrl)));
        });
        services.AddSingleton<IAuthorizationHandler, ResourceHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ResourceMismatchAsUnauthorizedHandler>();

        return services;
    }

    private static bool UsesOpenIddict(PathString path)
        => path.StartsWithSegments(McpPath, StringComparison.OrdinalIgnoreCase) || path.StartsWithSegments(WidgetPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>O token precisa ter o recurso do grupo de rotas (<c>aud</c>); <paramref name="Resource"/> recebe a URL pública do app.</summary>
    private sealed record ResourceRequirement(Func<string, string> Resource) : IAuthorizationRequirement;

    private sealed class ResourceHandler(IConfiguration configuration) : AuthorizationHandler<ResourceRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ResourceRequirement requirement)
        {
            var resource = requirement.Resource(OAuthSetup.ResolveAppUrl(configuration));
            if (context.User.HasClaim(Claims.Audience, resource))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Token válido, mas de outro recurso (do Claude no widget, ou do widget no /mcp): 401, como se não fosse aceito (FR-004).</summary>
    private sealed class ResourceMismatchAsUnauthorizedHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();

        public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<ResourceRequirement>().Any() == true)
            {
                var problem = ProblemFactory.ForStatus(StatusCodes.Status401Unauthorized);
                ProblemFactory.AddTraceId(problem, context);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\", error_description=\"O token não vale para este recurso.\"";
                await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", context.RequestAborted);
                return;
            }

            await _default.HandleAsync(next, context, policy, authorizeResult);
        }
    }

    private sealed class ConfigureJwtBearer(IOptions<JwtOptions> jwt) : IConfigureNamedOptions<JwtBearerOptions>
    {
        public void Configure(string? name, JwtBearerOptions options)
        {
            if (name != JwtBearerDefaults.AuthenticationScheme)
            {
                return;
            }

            Configure(options);
        }

        public void Configure(JwtBearerOptions options)
        {
            // Mantém `sub`, `sid`... como estão no token (sem o mapeamento para os nomes longos do WS-*).
            options.MapInboundClaims = false;
            options.TokenValidationParameters = jwt.Value.ToValidationParameters();
            options.Events = new JwtBearerEvents { OnChallenge = WriteProblemAsync };
        }

        private static async Task WriteProblemAsync(JwtBearerChallengeContext context)
        {
            context.HandleResponse();

            var expired = context.AuthenticateFailure is SecurityTokenExpiredException;
            var problem = expired
                ? ProblemFactory.ForStatus(StatusCodes.Status401Unauthorized, TokenExpiredCode, "Seu acesso expirou. Renove a sessão para continuar.")
                : ProblemFactory.ForStatus(StatusCodes.Status401Unauthorized);
            ProblemFactory.AddTraceId(problem, context.HttpContext);

            var response = context.Response;
            response.StatusCode = StatusCodes.Status401Unauthorized;
            response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
            await response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", context.HttpContext.RequestAborted);
        }
    }
}
