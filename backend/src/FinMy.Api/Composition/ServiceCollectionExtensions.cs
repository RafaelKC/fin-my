using FinMy.Api.Endpoints;
using FinMy.Api.Errors;
using FinMy.Application;
using FinMy.Application.Auth;
using FinMy.Infrastructure;
using FinMy.Infrastructure.Email;
using Microsoft.AspNetCore.HttpOverrides;

namespace FinMy.Api.Composition;

/// <summary>Raiz de composição: liga as camadas no DI (docs/arquitetura.md §1).</summary>
public static class ServiceCollectionExtensions
{
    public const string ConnectionName = "Default";

    /// <summary>Infraestrutura (banco, relógio, contexto, e-mail) com a configuração lida da seção <c>ConnectionStrings</c>/<c>Email</c>.</summary>
    public static IServiceCollection AddFinMyInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmtpEmailOptions>(configuration.GetSection(SmtpEmailOptions.SectionName));

        return services.AddInfrastructure(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"A string de conexão '{ConnectionName}' não está configurada (ConnectionStrings__{ConnectionName})."));
    }

    /// <summary>Application + Infrastructure + borda HTTP (erros, health, rate limiting, endpoints).</summary>
    public static IServiceCollection AddFinMyApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApplication();
        services.AddFinMyInfrastructure(configuration);
        services.AddFinMyAuthentication(configuration);

        // URL pública do app (links dos e-mails); APP_URL vem do compose.
        services.AddSingleton(new AppUrl(configuration["APP_URL"] is { Length: > 0 } appUrl ? appUrl : "http://localhost:8080"));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            if (!problem.Extensions.ContainsKey("code"))
            {
                // Respostas geradas pelo framework (404, 405, 415...) ganham code e detail em pt-BR.
                var padrao = ProblemFactory.ForStatus(problem.Status ?? context.HttpContext.Response.StatusCode);
                problem.Type = padrao.Type;
                problem.Title = padrao.Title;
                problem.Detail ??= padrao.Detail;
                problem.Extensions["code"] = padrao.Extensions["code"];
            }

            ProblemFactory.AddTraceId(problem, context.HttpContext);
        });

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        services.AddFinMyRateLimiting(configuration);

        // A API só é alcançada pelo nginx do compose (porta não publicada): confia no X-Forwarded-* dele
        // para que o limite por IP use o IP do cliente e não o do proxy.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.AddEndpointModulesFrom(typeof(IEndpointModule).Assembly);

        return services;
    }

    public static IServiceCollection AddEndpointModulesFrom(this IServiceCollection services, System.Reflection.Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEndpointModule).IsAssignableFrom(t)))
        {
            services.AddSingleton(typeof(IEndpointModule), type);
        }

        return services;
    }
}
