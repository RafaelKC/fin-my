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

        app.MapFinMyHealth();
        foreach (var module in app.Services.GetServices<IEndpointModule>())
        {
            module.MapEndpoints(app);
        }

        return app;
    }
}
