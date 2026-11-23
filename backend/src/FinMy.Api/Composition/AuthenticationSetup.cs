using FinMy.Api.Errors;
using FinMy.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinMy.Api.Composition;

/// <summary>Configuração do <c>JwtBearer</c> e dos erros 401 (<c>token_expired</c> quando o access token venceu).</summary>
public static class AuthenticationSetup
{
    public const string TokenExpiredCode = "token_expired";

    public static IServiceCollection AddFinMyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
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
