using FinMy.Api.Composition;
using FinMy.Application.Auth;

namespace FinMy.Api.Endpoints.Auth;

/// <summary>Corpo de <c>POST /auth/password-reset/request</c>.</summary>
public sealed record RequestPasswordResetBody(string? Email);

/// <summary>Corpo de <c>POST /auth/password-reset/confirm</c>.</summary>
public sealed record ConfirmPasswordResetBody(string? Token, string? NewPassword);

/// <summary>Recuperação de senha (US4): contracts/openapi.yaml <c>/auth/password-reset/*</c>.</summary>
public sealed class PasswordResetEndpoints : IEndpointModule
{
    /// <summary>Mensagem única do pedido, exista ou não a conta (FR-011).</summary>
    public const string RequestAcceptedDetail = "Se houver conta com este e-mail, enviaremos um link";

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/auth/password-reset")
            .WithTags("auth")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AllowAnonymous();

        group.MapPost("/request", async (RequestPasswordResetBody body, RequestPasswordResetHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(new RequestPasswordResetCommand(body.Email), cancellationToken);
            return Results.Json(new { detail = RequestAcceptedDetail }, statusCode: StatusCodes.Status202Accepted);
        });

        group.MapPost("/confirm", async (ConfirmPasswordResetBody body, ConfirmPasswordResetHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(new ConfirmPasswordResetCommand(body.Token, body.NewPassword), cancellationToken);
            return Results.NoContent();
        });
    }
}
