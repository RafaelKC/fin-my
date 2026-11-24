using FinMy.Application.Abstractions;
using FinMy.Application.Months;
using FinMy.Domain.Monetary;
using FinMy.Domain.Settlements;

namespace FinMy.Application.Panel;

/// <summary>Carteira ativa do Resumo de agora, com o saldo atual (002 FR-003).</summary>
public sealed record NowWallet(Guid Id, string Name, string Color, long CurrentBalanceCents);

/// <summary>Saldo acumulado de uma Tag de acerto até o mês de hoje (004 FR-008); negativo é crédito da pessoa.</summary>
public sealed record NowSettlement(Guid TagId, string TagName, long AccumulatedCents);

/// <summary>Resposta de <c>GET /now</c> (006 contracts/api.md).</summary>
public sealed record NowResult(
    DateOnly Today,
    IReadOnlyList<NowWallet> Wallets,
    long ArchivedWalletsBalanceCents,
    long TotalBalanceCents,
    IReadOnlyList<MonthInvoiceItem> Invoices,
    PendingSummary Pending,
    IReadOnlyList<NowSettlement> Settlements);

/// <summary>
/// <c>GET /now</c> (006 US2, research R2): saldos atuais, faturas de hoje − 30 a hoje + 30 dias com Restante &gt; 0, pendências e
/// Saldo acumulado das Tags de acerto até o mês de hoje, tudo reaproveitando as consultas das telas de origem.
/// </summary>
public sealed class GetNowHandler(IWalletStore wallets, INowReader now, ISettlementReader settlements, IClock clock)
{
    private const int WindowDays = 30;

    public async Task<NowResult> HandleAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;

        var all = await wallets.ListAsync(WalletArchivedFilter.All, cancellationToken);
        var invoices = await now.ReadInvoicesAsync(today.AddDays(-WindowDays), today.AddDays(WindowDays), today, cancellationToken);
        var pending = await now.ReadPendingAsync(today, cancellationToken);
        var month = YearMonth.From(today);
        var tags = await settlements.ReadAsync(month, cancellationToken);

        var settlementRows = tags
            .Select(t => new NowSettlement(t.TagId, t.Name, SettlementCalculator.Calculate(month, t.Months).AccumulatedToReceiveCents))
            .Where(s => s.AccumulatedCents != 0)
            .OrderBy(s => s.TagName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.TagId)
            .ToList();

        return new NowResult(
            today,
            all.Where(w => !w.Archived).Select(w => new NowWallet(w.Id, w.Name, w.Color, w.CurrentBalanceCents)).ToList(),
            all.Where(w => w.Archived).Sum(w => w.CurrentBalanceCents),
            all.Sum(w => w.CurrentBalanceCents),
            invoices,
            pending,
            settlementRows);
    }
}
