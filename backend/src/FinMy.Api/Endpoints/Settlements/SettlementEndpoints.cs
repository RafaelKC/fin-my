using FinMy.Application.Abstractions;
using FinMy.Application.Settlements;

namespace FinMy.Api.Endpoints.Settlements;

/// <summary>Linha de acerto (contracts/openapi.yaml <c>/settlements/{month}</c>); dinheiro em centavos.</summary>
public sealed record SettlementRowResponse(
    Guid TagId,
    string TagName,
    string TagColor,
    bool Archived,
    long SpentCents,
    long ChargedCents,
    long ReceivedCents,
    long ToChargeCents,
    long ToReceiveCents,
    long AccumulatedCents)
{
    public static SettlementRowResponse From(SettlementRow row) => new(
        row.TagId,
        row.TagName,
        row.TagColor,
        row.Archived,
        row.Figures.SpentCents,
        row.Figures.ChargedCents,
        row.Figures.ReceivedCents,
        row.Figures.ToChargeCents,
        row.Figures.ToReceiveCents,
        row.Figures.AccumulatedToReceiveCents);
}

/// <summary>Lançamento que compõe um número dos acertos; <c>group</c> é <c>spent</c> ou <c>charged</c>.</summary>
public sealed record SettlementItemResponse(
    string Group, string Type, Guid Id, string Description, DateOnly Date, long SignedCents, bool? Received, Guid? CreditCardId)
{
    public static SettlementItemResponse From(SettlementItem item) => new(
        item.Group == SettlementGroup.Spent ? "spent" : "charged",
        item.Type,
        item.Id,
        item.Description,
        item.Date,
        item.SignedCents,
        item.Received,
        item.CreditCardId);
}

/// <summary>Acertos por Tag de acerto (004 US2): <c>/settlements/*</c> de contracts/openapi.yaml.</summary>
public sealed class SettlementEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/settlements").WithTags("settlements").RequireAuthorization();

        group.MapGet("/{month}", async (string month, GetSettlementsHandler handler, HttpContext http, CancellationToken cancellationToken) =>
        {
            var rows = await handler.HandleAsync(month, cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(rows.Select(SettlementRowResponse.From));
        });

        group.MapGet("/{month}/tags/{tagId:guid}/items", async (string month, Guid tagId, GetSettlementItemsHandler handler, HttpContext http, CancellationToken cancellationToken) =>
        {
            var items = await handler.HandleAsync(month, tagId, cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(items.Select(SettlementItemResponse.From));
        });
    }
}
