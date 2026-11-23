using FinMy.Api.Endpoints.Transfers;
using FinMy.Api.Idempotency;
using FinMy.Application.Cards;
using Microsoft.AspNetCore.Mvc;

namespace FinMy.Api.Endpoints.Cards;

/// <summary>
/// Compras no cartão e parcelamentos (003 US2–US4, US7.2, US7.3): <c>/card-purchases*</c> e <c>/installment-plans/{id}</c>
/// de contracts/openapi.yaml.
/// </summary>
public sealed class CardPurchaseEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/card-purchases").WithTags("card-purchases").RequireAuthorization();

        group.MapPost("/", async (CardPurchaseCreateRequest body, CreateCardPurchaseHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(body.ToCommand(), cancellationToken);
            var purchases = result.Purchases.Select(CardPurchaseResponse.From).ToList();
            return Results.Created($"/api/v1/card-purchases/{purchases[0].Id}", new CardPurchaseCreateResponse(purchases, result.Warnings));
        }).WithIdempotency();

        group.MapGet("/{id:guid}", async (Guid id, GetCardPurchaseHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(CardPurchaseResponse.From(await handler.HandleAsync(id, cancellationToken))));

        group.MapPut("/{id:guid}", async (Guid id, CardPurchaseUpdateRequest body, UpdateCardPurchaseHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(CardPurchaseResponse.From(await handler.HandleAsync(body.ToCommand(id), cancellationToken))));

        group.MapDelete("/{id:guid}", async (Guid id, [FromQuery] string? scope, DeleteCardPurchaseHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(new DeletionResponse(await handler.HandleAsync(id, scope, cancellationToken))));

        group.MapPatch("/{id:guid}/reconciled", async (Guid id, CardPurchaseReconciledRequest body, ReconcileCardPurchaseHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(CardPurchaseResponse.From(await handler.HandleAsync(id, body.Reconciled, cancellationToken))));

        group.MapPost("/{id:guid}/move", async (Guid id, CardPurchaseMoveRequest body, MoveCardPurchaseHandler handler, CancellationToken cancellationToken) =>
            Results.Ok((await handler.HandleAsync(new MoveCardPurchaseCommand(id, body.Direction, body.Scope), cancellationToken)).Select(CardPurchaseResponse.From)));

        app.MapGroup("/api/v1/installment-plans").WithTags("installment-plans").RequireAuthorization()
            .MapPut("/{id:guid}", async (Guid id, InstallmentPlanUpdateRequest body, UpdateInstallmentPlanHandler handler, CancellationToken cancellationToken) =>
                Results.Ok((await handler.HandleAsync(new UpdateInstallmentPlanCommand(id, body.InstallmentTotal, body.Amount, body.AmountMode), cancellationToken))
                    .Select(CardPurchaseResponse.From)));
    }
}
