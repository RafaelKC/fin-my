using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Months;

/// <summary>Soma de lançamentos de um Mês de referência, por tipo e situação (agregado já somado pelo banco).</summary>
public sealed record EntryTotal(YearMonth Month, EntryKind Kind, EntryStatus Status, long Cents);

/// <summary>Soma das transferências de um mês (pela data) entre duas Carteiras.</summary>
public sealed record TransferTotal(YearMonth Month, Guid FromWalletId, Guid ToWalletId, long Cents);

/// <summary>Saldo inicial de uma Carteira selecionada.</summary>
public sealed record WalletOpening(Guid WalletId, long Cents, DateOnly Date);

/// <summary>
/// Valor de Faturas num mês (ponto de extensão da 003, RT-07): previsões entram nas Saídas do Previsto,
/// pagamentos nas Saídas do Realizado. Na 002 as listas chegam vazias.
/// </summary>
public sealed record InvoiceAmount(YearMonth Month, long Cents);

/// <summary>Uma coluna do resumo (Previsto ou Realizado), todos os valores em centavos.</summary>
public sealed record SummaryColumn(
    long OpeningBalanceCents,
    long WalletsInitialBalanceCents,
    long IncomeCents,
    long ExpenseCents,
    long ResultCents,
    long TransfersInCents,
    long TransfersOutCents,
    long FinalBalanceCents);

/// <summary>Resumo do mês nas duas leituras (RT-06).</summary>
public sealed record MonthSummary(SummaryColumn Planned, SummaryColumn Actual);

/// <summary>
/// Resumo do mês (002 FR-015, research R4). Puro: recebe agregados já somados e calcula
/// <c>final(M) = Σ saldo inicial (data ≤ fim de M) + Σ_{m ≤ M} resultado(m) + Σ_{m ≤ M} transferências líquidas(m)</c>
/// e <c>saldo inicial(M) = final(M) − resultado(M) − transferências líquidas(M)</c>, o que já inclui os saldos
/// iniciais de Carteiras com data em M (informados à parte em <see cref="SummaryColumn.WalletsInitialBalanceCents"/>).
/// </summary>
public static class MonthSummaryCalculator
{
    /// <param name="month">Mês M.</param>
    /// <param name="wallets">Saldos iniciais das Carteiras selecionadas (qualquer data).</param>
    /// <param name="entries">Totais de lançamentos das Carteiras selecionadas (meses até M; os posteriores são ignorados).</param>
    /// <param name="transfers">Totais de transferências que tocam alguma Carteira selecionada.</param>
    /// <param name="invoiceForecasts">Extensão 003: valor das Faturas por mês de vencimento (RT-07); vazio na 002.</param>
    /// <param name="invoicePayments">Extensão 003: pagamentos de Fatura por Mês de referência; vazio na 002.</param>
    public static MonthSummary Calculate(
        YearMonth month,
        IReadOnlyCollection<WalletOpening> wallets,
        IReadOnlyCollection<EntryTotal> entries,
        IReadOnlyCollection<TransferTotal> transfers,
        IReadOnlyCollection<InvoiceAmount> invoiceForecasts,
        IReadOnlyCollection<InvoiceAmount> invoicePayments)
    {
        ArgumentNullException.ThrowIfNull(wallets);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(invoiceForecasts);
        ArgumentNullException.ThrowIfNull(invoicePayments);

        var selected = wallets.Select(w => w.WalletId).ToHashSet();
        var upToMonth = wallets.Where(w => w.Date <= month.LastDay).ToList();
        var initialUpToMonth = upToMonth.Sum(w => w.Cents);
        var initialInMonth = upToMonth.Where(w => month.Contains(w.Date)).Sum(w => w.Cents);

        // Transferências só pesam quando exatamente um dos lados está selecionado (RT-08).
        var transfersUpToMonth = transfers.Where(t => t.Month <= month).ToList();
        var transfersIn = TransfersTotal(transfersUpToMonth, month, t => selected.Contains(t.ToWalletId) && !selected.Contains(t.FromWalletId));
        var transfersOut = TransfersTotal(transfersUpToMonth, month, t => selected.Contains(t.FromWalletId) && !selected.Contains(t.ToWalletId));
        var transfersNetUpToMonth = NetUpToMonth(transfersUpToMonth, selected);

        SummaryColumn Column(bool settledOnly, IReadOnlyCollection<InvoiceAmount> invoices)
        {
            var scoped = entries.Where(e => e.Month <= month && (!settledOnly || e.Status == EntryStatus.Settled)).ToList();
            var incomeToDate = scoped.Where(e => e.Kind == EntryKind.Income).Sum(e => e.Cents);
            var expenseToDate = scoped.Where(e => e.Kind == EntryKind.Expense).Sum(e => e.Cents) + invoices.Where(i => i.Month <= month).Sum(i => i.Cents);

            var incomeInMonth = scoped.Where(e => e.Month == month && e.Kind == EntryKind.Income).Sum(e => e.Cents);
            var expenseInMonth = scoped.Where(e => e.Month == month && e.Kind == EntryKind.Expense).Sum(e => e.Cents)
                + invoices.Where(i => i.Month == month).Sum(i => i.Cents);
            var result = incomeInMonth - expenseInMonth;

            var final = initialUpToMonth + (incomeToDate - expenseToDate) + transfersNetUpToMonth;
            var transfersNetInMonth = transfersIn - transfersOut;

            return new SummaryColumn(
                OpeningBalanceCents: final - result - transfersNetInMonth,
                WalletsInitialBalanceCents: initialInMonth,
                IncomeCents: incomeInMonth,
                ExpenseCents: expenseInMonth,
                ResultCents: result,
                TransfersInCents: transfersIn,
                TransfersOutCents: transfersOut,
                FinalBalanceCents: final);
        }

        return new MonthSummary(Column(settledOnly: false, invoiceForecasts), Column(settledOnly: true, invoicePayments));
    }

    private static long TransfersTotal(List<TransferTotal> transfers, YearMonth month, Func<TransferTotal, bool> counts) =>
        transfers.Where(t => t.Month == month && counts(t)).Sum(t => t.Cents);

    private static long NetUpToMonth(List<TransferTotal> transfers, HashSet<Guid> selected) =>
        transfers.Sum(t => selected.Contains(t.ToWalletId) == selected.Contains(t.FromWalletId)
            ? 0
            : selected.Contains(t.ToWalletId) ? t.Cents : -t.Cents);
}
