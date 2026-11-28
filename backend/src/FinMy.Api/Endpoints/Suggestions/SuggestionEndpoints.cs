using FinMy.Application.Suggestions;

namespace FinMy.Api.Endpoints.Suggestions;

public sealed record SuggestionResponse(
    Guid Id, string Source, string? Bank, string Kind, string Description, long AmountCents, string AmountText,
    DateTimeOffset OccurredAt, string? CardLast4, string Status, Guid? SuggestedCreditCardId, Guid? SuggestedWalletId)
{
    public static SuggestionResponse From(SuggestionView v) => new(
        v.Id, v.Source, v.Bank, v.Kind, v.Description, v.AmountCents, FinMy.Application.Cards.InvoiceErrors.FormatBrl(v.AmountCents),
        v.OccurredAt, v.CardLast4, v.Status, v.SuggestedCreditCardId, v.SuggestedWalletId);
}

public sealed record ResolveSuggestionRequest(Guid? LinkedId);

/// <summary>Sugestões de lançamento lidas das notificações de banco (010): lista, detalhe, lançada e descartar.</summary>
public sealed class SuggestionEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/suggestions").WithTags("suggestions").RequireAuthorization();

        group.MapGet("/", async (ListSuggestionsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok((await handler.HandleAsync(cancellationToken)).Select(SuggestionResponse.From)));

        group.MapGet("/{id:guid}", async (Guid id, GetSuggestionHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(SuggestionResponse.From(await handler.HandleAsync(id, cancellationToken))));

        group.MapPost("/{id:guid}/added", async (Guid id, ResolveSuggestionRequest? body, ResolveSuggestionHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(id, added: true, body?.LinkedId, cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/dismiss", async (Guid id, ResolveSuggestionHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(id, added: false, null, cancellationToken);
            return Results.NoContent();
        });
    }
}
