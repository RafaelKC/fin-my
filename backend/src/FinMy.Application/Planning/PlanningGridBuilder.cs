using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Planning;

/// <summary>Monta as linhas da grade do ano a partir dos lançamentos (007 research R2, FR-002/FR-003). Puro.</summary>
public static class PlanningGridBuilder
{
    public static IReadOnlyList<PlanningRow> Rows(EntryKind kind, int year, PlanningData data) =>
        data.Entries
            .Where(e => e.Kind == kind)
            .GroupBy(e => e.DescriptionNormalized)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Row(kind, year, g.Key, g.ToList(), data.ActiveWalletIds))
            .ToList();

    private static PlanningRow Row(EntryKind kind, int year, string key, List<PlanningEntry> items, IReadOnlySet<Guid> activeWallets)
    {
        // Mais recente: maior data, depois ordem de criação (o id é UUIDv7, também cronológico).
        var latest = items.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).First();
        var cells = Enumerable.Range(1, 12).Select(m => Cell(YearMonth.Of(year, m), items)).ToList();
        return new PlanningRow(
            kind,
            $"{(kind == EntryKind.Income ? "income" : "expense")}:{key}",
            latest.Description,
            activeWallets.Contains(latest.WalletId) ? latest.WalletId : null,
            latest.Date.Day,
            latest.TagIds,
            cells);
    }

    private static PlanningCell Cell(YearMonth month, List<PlanningEntry> items)
    {
        var inMonth = items.Where(e => e.Month == month).ToList();
        var single = inMonth.Count == 1 ? inMonth[0] : null;
        return new PlanningCell(
            month,
            inMonth.Sum(e => e.AmountCents),
            inMonth.Count,
            inMonth.Count > 0 && inMonth.All(e => e.Status == EntryStatus.Settled),
            inMonth.Count == 0 || single?.Status == EntryStatus.Planned,
            single?.Id);
    }

    /// <summary>Um cartão por linha: ativos sempre; arquivados só com fatura (total, pagamento ou estimativa) no ano.</summary>
    public static IReadOnlyList<InvoiceRow> InvoiceRows(int year, PlanningData data) =>
        data.Cards
            .Where(c => !c.Archived || data.Invoices.Any(i => i.CreditCardId == c.Id))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Id)
            .Select(c => new InvoiceRow(
                c.Id,
                c.Name,
                c.Archived,
                Enumerable.Range(1, 12)
                    .Select(m => YearMonth.Of(year, m))
                    .Select(month => data.Invoices.FirstOrDefault(i => i.CreditCardId == c.Id && i.DueMonth == month) is { } i
                        ? new InvoiceCell(month, i.PlannedCents, i.TotalCents, i.EstimateCents)
                        : new InvoiceCell(month, 0, 0, null))
                    .ToList()))
            .ToList();
}
