using FinMy.Application.Onboarding;

namespace FinMy.Api.Endpoints.Onboarding;

/// <param name="State"><c>done</c> ou <c>skipped</c>.</param>
public sealed record OnboardingStepRequest(string? State);

/// <summary>Resposta de <c>/onboarding</c> (006 contracts/api.md).</summary>
public sealed record OnboardingDto(IReadOnlyDictionary<string, string> Steps, bool Dismissed, bool AutoStart)
{
    public static OnboardingDto From(OnboardingState s) => new(s.Steps, s.Dismissed, s.AutoStart);
}

/// <summary>Guia de primeiros passos (006 US3): <c>GET /onboarding</c>, <c>PUT /onboarding/steps/{step}</c>, <c>POST /onboarding/dismiss</c>.</summary>
public sealed class OnboardingEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/onboarding").WithTags("onboarding").RequireAuthorization();

        group.MapGet("", async (GetOnboardingHandler handler, HttpContext http, CancellationToken cancellationToken) =>
        {
            var state = await handler.HandleAsync(cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(OnboardingDto.From(state));
        });

        group.MapPut("/steps/{step}", async (string step, OnboardingStepRequest? body, SetOnboardingStepHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(OnboardingDto.From(await handler.HandleAsync(step, body?.State, cancellationToken))));

        group.MapPost("/dismiss", async (DismissOnboardingHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(OnboardingDto.From(await handler.HandleAsync(cancellationToken))));
    }
}
