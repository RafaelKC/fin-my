using FinMy.Api.Endpoints;
using FinMy.Infrastructure.Context;
using Serilog;

namespace FinMy.Api.Composition;

public static class WebApplicationExtensions
{
    /// <summary>Pipeline HTTP na ordem: proxy → log → erros → rate limiting → autenticação → sessão → contexto → autorização → endpoints.</summary>
    public static WebApplication UseFinMyPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseSessionValidation();
        app.UseCurrentContext();
        app.UseAuthorization();

        // O contrato gerado (/openapi/v1.json) alimenta a suíte de isolamento; não é publicado em produção.
        if (!app.Environment.IsProduction())
        {
            app.MapOpenApi();
        }

        app.MapFinMyHealth();
        foreach (var module in app.Services.GetServices<IEndpointModule>())
        {
            module.MapEndpoints(app);
        }

        return app;
    }
}
