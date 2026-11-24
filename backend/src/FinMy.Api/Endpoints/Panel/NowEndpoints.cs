using FinMy.Api.Endpoints.Months;
using FinMy.Application.Panel;
using FinMy.Domain.Cards;

namespace FinMy.Api.Endpoints.Panel;

public sealed record NowWalletDto(Guid Id, string Name, string Color, long CurrentBalanceCents);

public sealed record NowInvoiceDto(
    Guid CreditCardId, string CreditCardName, string DueMonth, DateOnly DueDate, string Status, long RemainingCents);

public sealed record NowSettlementDto(Guid TagId, string TagName, long AccumulatedCents);

/// <summary>Resposta de <c>GET /now</c> (006 contracts/api.md).</summary>
public sealed record NowDto(
    DateOnly Today,
    IReadOnlyList<NowWalletDto> Wallets,
    long ArchivedWalletsBalanceCents,
    long TotalBalanceCents,
    IReadOnlyList<NowInvoiceDto> Invoices,
    PendingDto Pending,
    IReadOnlyList<NowSettlementDto> Settlements)
{
    public static NowDto From(NowResult r) => new(
        r.Today,
        r.Wallets.Select(w => new NowWalletDto(w.Id, w.Name, w.Color, w.CurrentBalanceCents)).ToList(),
        r.ArchivedWalletsBalanceCents,
        r.TotalBalanceCents,
        r.Invoices.Select(i => new NowInvoiceDto(
            i.CreditCardId, i.CreditCardName, i.DueMonth.ToString(), i.DueDate, i.Status.ToCode(), i.RemainingCents)).ToList(),
        new PendingDto(r.Pending.Count, r.Pending.ToReceiveCents, r.Pending.ToPayCents),
        r.Settlements.Select(s => new NowSettlementDto(s.TagId, s.TagName, s.AccumulatedCents)).ToList());
}

/// <summary>Resumo de agora (006 US2): <c>GET /api/v1/now</c>.</summary>
public sealed class NowEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/now", async (GetNowHandler handler, HttpContext http, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(NowDto.From(result));
        }).WithTags("panel").RequireAuthorization();
    }
}
