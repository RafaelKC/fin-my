using FinMy.Api.Endpoints;
using Serilog;

namespace FinMy.Api.Composition;

public static class WebApplicationExtensions
{
    /// <summary>Pipeline HTTP na ordem: proxy → log → erros → rate limiting → endpoints.</summary>
    public static WebApplication UseFinMyPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRouting();
        app.UseRateLimiter();

        app.MapFinMyHealth();
        foreach (var module in app.Services.GetServices<IEndpointModule>())
        {
            module.MapEndpoints(app);
        }

        return app;
    }
}
