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
/// Uma Fatura vista pela visão do mês (003 FR-022/FR-023, research R8): total, pago por Carteira e Carteira de pagamento
/// do cartão, por mês de vencimento. Os valores no recorte de Carteiras saem de <see cref="Planned"/> e <see cref="Actual"/>.
/// <paramref name="ActiveEstimateCents"/> é a Estimativa (007) só quando ainda vale (hoje &lt; fechamento); <c>null</c> caso contrário.
/// </summary>
public sealed record InvoiceFigure(
    YearMonth DueMonth,
    long TotalCents,
    Guid PaymentWalletId,
    IReadOnlyDictionary<Guid, long> PaidByWallet,
    long? ActiveEstimateCents = null)
{
    /// <summary>Total pago, com qualquer Carteira.</summary>
    public long PaidCents => PaidByWallet.Values.Sum();

    /// <summary>Restante: total menos pago (pode ser negativo).</summary>
    public long RemainingCents => TotalCents - PaidCents;

    /// <summary>Realizado: pagamentos feitos com as Carteiras selecionadas (FR-023).</summary>
    public long Actual(IReadOnlySet<Guid> selected) => PaidByWallet.Where(p => selected.Contains(p.Key)).Sum(p => p.Value);

    /// <summary>
    /// Acréscimo que a Estimativa dá ao Previsto: <c>max(Total, Pago, Estimativa, 0) − max(Total, Pago, 0)</c> (007 FR-010).
    /// </summary>
    public long EstimateLiftCents =>
        ActiveEstimateCents is { } estimate ? Math.Max(estimate - Math.Max(Math.Max(TotalCents, PaidCents), 0), 0) : 0;

    /// <summary>
    /// Previsto: pagamentos com as Carteiras selecionadas, mais o maior entre Restante e 0 se a Carteira de pagamento do cartão
    /// estiver entre elas (FR-023). Com todas as Carteiras vale <c>max(Total, Pago, 0)</c> (FR-022). O acréscimo da Estimativa
    /// (007 FR-011) também vai para a Carteira de pagamento do cartão.
    /// </summary>
    public long Planned(IReadOnlySet<Guid> selected) =>
        Actual(selected) + (selected.Contains(PaymentWalletId) ? Math.Max(RemainingCents, 0) + EstimateLiftCents : 0);
}

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
    /// <param name="invoices">Faturas por mês de vencimento (003 RT-07): o valor no Previsto e os pagamentos no Realizado entram nas Saídas.</param>
    public static MonthSummary Calculate(
        YearMonth month,
        IReadOnlyCollection<WalletOpening> wallets,
        IReadOnlyCollection<EntryTotal> entries,
        IReadOnlyCollection<TransferTotal> transfers,
        IReadOnlyCollection<InvoiceFigure> invoices)
    {
        ArgumentNullException.ThrowIfNull(wallets);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(invoices);

        var selected = wallets.Select(w => w.WalletId).ToHashSet();
        var upToMonth = wallets.Where(w => w.Date <= month.LastDay).ToList();
        var initialUpToMonth = upToMonth.Sum(w => w.Cents);
        var initialInMonth = upToMonth.Where(w => month.Contains(w.Date)).Sum(w => w.Cents);

        // Transferências só pesam quando exatamente um dos lados está selecionado (RT-08).
        var transfersUpToMonth = transfers.Where(t => t.Month <= month).ToList();
        var transfersIn = TransfersTotal(transfersUpToMonth, month, t => selected.Contains(t.ToWalletId) && !selected.Contains(t.FromWalletId));
        var transfersOut = TransfersTotal(transfersUpToMonth, month, t => selected.Contains(t.FromWalletId) && !selected.Contains(t.ToWalletId));
        var transfersNetUpToMonth = NetUpToMonth(transfersUpToMonth, selected);

        SummaryColumn Column(bool settledOnly)
        {
            var invoiceValues = invoices
                .Where(i => i.DueMonth <= month)
                .Select(i => (i.DueMonth, Cents: settledOnly ? i.Actual(selected) : i.Planned(selected)))
                .ToList();
            var scoped = entries.Where(e => e.Month <= month && (!settledOnly || e.Status == EntryStatus.Settled)).ToList();
            var incomeToDate = scoped.Where(e => e.Kind == EntryKind.Income).Sum(e => e.Cents);
            var expenseToDate = scoped.Where(e => e.Kind == EntryKind.Expense).Sum(e => e.Cents) + invoiceValues.Sum(i => i.Cents);

            var incomeInMonth = scoped.Where(e => e.Month == month && e.Kind == EntryKind.Income).Sum(e => e.Cents);
            var expenseInMonth = scoped.Where(e => e.Month == month && e.Kind == EntryKind.Expense).Sum(e => e.Cents)
                + invoiceValues.Where(i => i.DueMonth == month).Sum(i => i.Cents);
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

        return new MonthSummary(Column(settledOnly: false), Column(settledOnly: true));
    }

    private static long TransfersTotal(List<TransferTotal> transfers, YearMonth month, Func<TransferTotal, bool> counts) =>
        transfers.Where(t => t.Month == month && counts(t)).Sum(t => t.Cents);

    private static long NetUpToMonth(List<TransferTotal> transfers, HashSet<Guid> selected) =>
        transfers.Sum(t => selected.Contains(t.ToWalletId) == selected.Contains(t.FromWalletId)
            ? 0
            : selected.Contains(t.ToWalletId) ? t.Cents : -t.Cents);
}
