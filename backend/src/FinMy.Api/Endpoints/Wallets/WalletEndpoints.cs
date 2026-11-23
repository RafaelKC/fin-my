using FinMy.Api.Idempotency;
using FinMy.Application.Wallets;
using Microsoft.AspNetCore.Mvc;

namespace FinMy.Api.Endpoints.Wallets;

/// <summary>Carteiras (002 US1): <c>/wallets*</c> de contracts/openapi.yaml.</summary>
public sealed class WalletEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/wallets").WithTags("wallets").RequireAuthorization();

        group.MapGet("/", async ([FromQuery] string? archived, ListWalletsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok((await handler.HandleAsync(archived, cancellationToken)).Select(WalletResponse.From)));

        group.MapPost("/", async (WalletRequest body, CreateWalletHandler handler, CancellationToken cancellationToken) =>
        {
            var wallet = WalletResponse.From(await handler.HandleAsync(body.ToInput(), cancellationToken));
            return Results.Created($"/api/v1/wallets/{wallet.Id}", wallet);
        }).WithIdempotency();

        group.MapGet("/{id:guid}", async (Guid id, GetWalletHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(WalletResponse.From(await handler.HandleAsync(id, cancellationToken))));

        group.MapPut("/{id:guid}", async (Guid id, UpdateWalletRequest body, UpdateWalletHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(WalletResponse.From(await handler.HandleAsync(id, body.ToInput(), body.Version, cancellationToken))));

        group.MapDelete("/{id:guid}", async (Guid id, [FromQuery] Guid? newPrimaryWalletId, DeleteWalletHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(id, newPrimaryWalletId, cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/make-primary", async (Guid id, MakePrimaryWalletHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(WalletResponse.From(await handler.HandleAsync(id, cancellationToken))));

        group.MapPost("/{id:guid}/archive", async (Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ArchiveWalletRequest? body, ArchiveWalletHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(WalletResponse.From(await handler.HandleAsync(id, body?.NewPrimaryWalletId, cancellationToken))));

        group.MapPost("/{id:guid}/unarchive", async (Guid id, UnarchiveWalletHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(WalletResponse.From(await handler.HandleAsync(id, cancellationToken))));
    }
}
