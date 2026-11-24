using FinMy.Api.Errors;
using FinMy.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Validation.AspNetCore;

namespace FinMy.Api.Composition;

/// <summary>Configuração do <c>JwtBearer</c> e dos erros 401 (<c>token_expired</c> quando o access token venceu).</summary>
public static class AuthenticationSetup
{
    public const string SelectorScheme = "FinMy";
    public const string McpPath = "/mcp";
    public const string TokenExpiredCode = "token_expired";

    public static IServiceCollection AddFinMyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateOnStart();

        // O app usa o JWT próprio; o conector do Claude (/mcp) usa os tokens do OpenIddict (008). Cada rota só aceita um deles:
        // um token do conector não alcança a API REST e um JWT do app não alcança o /mcp.
        services.AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, displayName: null, options => options.ForwardDefaultSelector = context =>
                context.Request.Path.StartsWithSegments(McpPath, StringComparison.OrdinalIgnoreCase)
                    ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                    : JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddFinMyOAuth(configuration);
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();
        services.AddAuthorization();

        return services;
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
