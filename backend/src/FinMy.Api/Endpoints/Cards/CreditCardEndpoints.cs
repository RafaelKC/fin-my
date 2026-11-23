using FinMy.Api.Idempotency;
using FinMy.Application.Cards;
using Microsoft.AspNetCore.Mvc;

namespace FinMy.Api.Endpoints.Cards;

/// <summary>Cartões de crédito (003 US1, US6): <c>/credit-cards*</c> de contracts/openapi.yaml.</summary>
public sealed class CreditCardEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/credit-cards").WithTags("credit-cards").RequireAuthorization();

        group.MapGet("/", async ([FromQuery] string? archived, ListCreditCardsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok((await handler.HandleAsync(archived, cancellationToken)).Select(CreditCardOverviewResponse.From)));

        group.MapPost("/", async (CreditCardRequest body, CreateCreditCardHandler handler, CancellationToken cancellationToken) =>
        {
            var card = CreditCardResponse.From(await handler.HandleAsync(body.ToInput(), cancellationToken));
            return Results.Created($"/api/v1/credit-cards/{card.Id}", card);
        }).WithIdempotency();

        group.MapGet("/{id:guid}", async (Guid id, GetCreditCardHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(CreditCardOverviewResponse.From(await handler.HandleAsync(id, cancellationToken))));

        group.MapPut("/{id:guid}", async (Guid id, UpdateCreditCardRequest body, UpdateCreditCardHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(CreditCardResponse.From(await handler.HandleAsync(id, body.ToInput(), body.Version, cancellationToken))));

        group.MapDelete("/{id:guid}", async (Guid id, DeleteCreditCardHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(id, cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/archive", async (Guid id, ArchiveCreditCardHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(CreditCardResponse.From(await handler.HandleAsync(id, cancellationToken))));

        group.MapPost("/{id:guid}/unarchive", async (Guid id, UnarchiveCreditCardHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(CreditCardResponse.From(await handler.HandleAsync(id, cancellationToken))));

        // `date` chega como texto para o cartão ser conferido antes do formato (id de outro Espaço → 404).
        group.MapGet("/{id:guid}/invoice-for", async (Guid id, [FromQuery] string? date, GetInvoiceForHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(InvoiceRefResponse.From(await handler.HandleAsync(id, date, cancellationToken))));
    }
}
