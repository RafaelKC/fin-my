using FinMy.Application.Abstractions;
using FinMy.Application.Months;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Months;
using FinMy.Domain.Tags;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Months;

/// <summary>
/// Consultas da visão do mês (research R4): uma agregada por tipo de dado (lançamentos por mês/tipo/situação,
/// transferências por mês e carteiras) mais as listas do mês. Nada fica desnormalizado.
/// </summary>
public sealed class MonthReader(FinMyDbContext db) : IMonthReader
{
    public async Task<MonthData> ReadAsync(
        YearMonth month, IReadOnlyCollection<Guid>? walletIds, IReadOnlyCollection<Guid> tagIds, DateOnly today, CancellationToken cancellationToken)
    {
        var aggregates = await ReadAggregatesAsync(month, walletIds, today, cancellationToken);
        var wallets = aggregates.Wallets;
        var selected = wallets.Select(w => w.WalletId).ToList();

        var first = month.FirstDay;
        var last = month.LastDay;
        var currentMonth = YearMonth.From(today).FirstDay;

        var entries = db.Set<Entry>().AsNoTracking().Where(e => selected.Contains(e.WalletId));
        var transfers = db.Set<Transfer>().AsNoTracking()
            .Where(t => selected.Contains(t.FromWalletId) || selected.Contains(t.ToWalletId));
        var invoices = aggregates.Invoices;

        // RT-12: data crescente; empate pela ordem de criação (o id é UUIDv7, também cronológico).
        var listed = entries.Where(e => e.ReferenceMonth == first);
        if (tagIds.Count > 0)
        {
            listed = listed.Where(e =>
                (e.SettlementTagId != null && tagIds.Contains(e.SettlementTagId.Value))
                || db.Set<EntryTag>().Any(l => l.EntryId == e.Id && tagIds.Contains(l.TagId)));
        }

        var entryRows = await listed
            .OrderBy(e => e.EntryDate).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new { Entry = e, Version = EF.Property<uint>(e, "xmin") })
            .ToListAsync(cancellationToken);
        var entryLinks = await TagLinks.ForEntriesAsync(db, entryRows.Select(r => r.Entry.Id).ToList(), cancellationToken);
        var entryCounts = await SplitCounts.ForEntriesAsync(db, entryRows.Select(r => r.Entry.SplitGroupId), cancellationToken);
        var entryItems = entryRows
            .Select(r => new MonthEntryItem(
                r.Entry.Id, r.Entry.Kind, r.Entry.Description, r.Entry.AmountCents, r.Entry.EntryDate, r.Entry.ReferenceMonth, r.Entry.Status,
                r.Entry.WalletId, r.Entry.Notes, r.Entry.CreatedAt, r.Entry.CreatedVia, r.Version,
                TagLinks.All(r.Entry.SettlementTagId, entryLinks, r.Entry.Id), r.Entry.SettlementTagId,
                r.Entry.SplitGroupId, SplitCounts.Of(r.Entry.SplitGroupId, entryCounts),
                r.Entry.RecurrenceId, r.Entry.RecurrenceMonth, r.Entry.RecurrenceModified))
            .ToList();

        // Com filtro de tags, a visão mostra só itens com tag: sem transferências, e com as compras das faturas do mês.
        var transferItems = tagIds.Count > 0
            ? []
            : await transfers
                .Where(t => t.TransferDate >= first && t.TransferDate <= last)
                .OrderBy(t => t.TransferDate).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .Select(t => new MonthTransferItem(
                    t.Id, t.FromWalletId, t.ToWalletId, t.AmountCents, t.TransferDate, t.Description, t.Notes,
                    t.CreatedAt, t.CreatedVia, EF.Property<uint>(t, "xmin")))
                .ToListAsync(cancellationToken);

        var purchaseItems = tagIds.Count > 0 ? await ReadPurchasesAsync(month, tagIds, cancellationToken) : [];

        // Pendências (research R9) ignoram o filtro de carteiras: valem para o Espaço todo.
        var pending = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.Status == EntryStatus.Planned && e.ReferenceMonth < currentMonth)
            .GroupBy(e => e.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count(), Cents = g.Sum(e => e.AmountCents) })
            .ToListAsync(cancellationToken);

        return new MonthData(
            wallets,
            aggregates.EntryTotals,
            aggregates.TransferTotals,
            invoices.Figures,
            entryItems,
            transferItems,
            tagIds.Count > 0 ? [] : invoices.Lines,
            new PendingSummary(
                pending.Sum(p => p.Count),
                pending.Where(p => p.Kind == EntryKind.Income).Sum(p => p.Cents),
                pending.Where(p => p.Kind == EntryKind.Expense).Sum(p => p.Cents)),
            purchaseItems);
    }

    /// <summary>
    /// Painel do ano (006 research R1): as mesmas consultas agregadas do mês, uma vez só, até dezembro de <paramref name="year"/>;
    /// o <see cref="MonthSummaryCalculator"/> compõe os 12 meses. Sem filtro de Carteiras.
    /// </summary>
    public async Task<YearData> ReadYearAsync(int year, DateOnly today, CancellationToken cancellationToken)
    {
        var aggregates = await ReadAggregatesAsync(YearMonth.Of(year, 12), null, today, cancellationToken);
        return new YearData(aggregates.Wallets, aggregates.EntryTotals, aggregates.TransferTotals, aggregates.Invoices.Figures);
    }

    private sealed record Aggregates(
        IReadOnlyList<WalletOpening> Wallets,
        IReadOnlyList<EntryTotal> EntryTotals,
        IReadOnlyList<TransferTotal> TransferTotals,
        InvoiceRead Invoices);

    /// <summary>Agregados que alimentam o <see cref="MonthSummaryCalculator"/> para meses até <paramref name="month"/>.</summary>
    private async Task<Aggregates> ReadAggregatesAsync(
        YearMonth month, IReadOnlyCollection<Guid>? walletIds, DateOnly today, CancellationToken cancellationToken)
    {
        var wallets = await db.Set<Wallet>().AsNoTracking()
            .Where(w => walletIds == null || walletIds.Contains(w.Id))
            .Select(w => new WalletOpening(w.Id, w.InitialBalanceCents, w.InitialBalanceDate))
            .ToListAsync(cancellationToken);
        var selected = wallets.Select(w => w.WalletId).ToList();

        var first = month.FirstDay;
        var last = month.LastDay;

        var entryTotals = (await db.Set<Entry>().AsNoTracking()
                .Where(e => selected.Contains(e.WalletId) && e.ReferenceMonth <= first)
                .GroupBy(e => new { e.ReferenceMonth, e.Kind, e.Status })
                .Select(g => new { g.Key.ReferenceMonth, g.Key.Kind, g.Key.Status, Cents = g.Sum(e => e.AmountCents) })
                .ToListAsync(cancellationToken))
            .Select(r => new EntryTotal(YearMonth.From(r.ReferenceMonth), r.Kind, r.Status, r.Cents))
            .ToList();

        var transferTotals = (await db.Set<Transfer>().AsNoTracking()
                .Where(t => (selected.Contains(t.FromWalletId) || selected.Contains(t.ToWalletId)) && t.TransferDate <= last)
                .GroupBy(t => new { t.TransferDate.Year, t.TransferDate.Month, t.FromWalletId, t.ToWalletId })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.FromWalletId, g.Key.ToWalletId, Cents = g.Sum(t => t.AmountCents) })
                .ToListAsync(cancellationToken))
            .Select(r => new TransferTotal(YearMonth.Of(r.Year, r.Month), r.FromWalletId, r.ToWalletId, r.Cents))
            .ToList();

        var invoices = await ReadInvoicesAsync(first, month, selected, filtered: walletIds is not null, today, cancellationToken);
        return new Aggregates(wallets, entryTotals, transferTotals, invoices);
    }

    /// <summary>Compras das faturas que vencem em <paramref name="month"/> com qualquer das tags, na ordem RT-12.</summary>
    private async Task<IReadOnlyList<CardPurchaseView>> ReadPurchasesAsync(YearMonth month, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        var rows = await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => db.Set<Invoice>().Any(i => i.Id == p.InvoiceId && i.DueMonth == month.FirstDay))
            .Where(p =>
                (p.SettlementTagId != null && tagIds.Contains(p.SettlementTagId.Value))
                || db.Set<CardPurchaseTag>().Any(l => l.CardPurchaseId == p.Id && tagIds.Contains(l.TagId)))
            .OrderBy(p => p.PurchaseDate).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new { Purchase = p, Version = EF.Property<uint>(p, "xmin") })
            .ToListAsync(cancellationToken);
        var links = await TagLinks.ForPurchasesAsync(db, rows.Select(r => r.Purchase.Id).ToList(), cancellationToken);
        var counts = await SplitCounts.ForPurchasesAsync(db, rows.Select(r => r.Purchase.SplitGroupId), cancellationToken);
        return rows
            .Select(r => CardPurchaseStore.ToView(
                r.Purchase, month, r.Version, TagLinks.All(r.Purchase.SettlementTagId, links, r.Purchase.Id), SplitCounts.Of(r.Purchase.SplitGroupId, counts)))
            .ToList();
    }

    private sealed record InvoiceRead(IReadOnlyList<InvoiceFigure> Figures, IReadOnlyList<MonthInvoiceItem> Lines);

    /// <summary>
    /// Faturas com vencimento até o mês (research R8): total das compras e pagamentos por Carteira, agregados no banco;
    /// só entram faturas com total ≠ 0 ou com pagamentos. Com filtro, a linha do mês só aparece se alguma Carteira selecionada a toca.
    /// </summary>
    private async Task<InvoiceRead> ReadInvoicesAsync(
        DateOnly first, YearMonth month, List<Guid> selected, bool filtered, DateOnly today, CancellationToken cancellationToken)
    {
        var totals = await db.Set<CardPurchase>().AsNoTracking()
            .Join(db.Set<Invoice>().AsNoTracking().Where(i => i.DueMonth <= first), p => p.InvoiceId, i => i.Id, (p, i) => p)
            .GroupBy(p => p.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Cents = g.Sum(p => p.Nature == PurchaseNature.Credit ? -p.AmountCents : p.AmountCents) })
            .ToDictionaryAsync(r => r.InvoiceId, r => r.Cents, cancellationToken);

        var paid = (await db.Set<InvoicePayment>().AsNoTracking()
                .Join(db.Set<Invoice>().AsNoTracking().Where(i => i.DueMonth <= first), p => p.InvoiceId, i => i.Id, (p, i) => p)
                .GroupBy(p => new { p.InvoiceId, p.WalletId })
                .Select(g => new { g.Key.InvoiceId, g.Key.WalletId, Cents = g.Sum(p => p.AmountCents) })
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.InvoiceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<Guid, long>)g.ToDictionary(r => r.WalletId, r => r.Cents));

        var ids = totals.Where(t => t.Value != 0).Select(t => t.Key).Concat(paid.Keys).Distinct().ToList();
        var rows = await db.Set<Invoice>().AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Join(db.Set<CreditCard>().AsNoTracking(), i => i.CreditCardId, c => c.Id, (i, c) => new { Invoice = i, c.Name, c.PaymentWalletId })
            .ToListAsync(cancellationToken);

        var set = selected.ToHashSet();
        var none = new Dictionary<Guid, long>();
        var figures = new List<InvoiceFigure>();
        var lines = new List<MonthInvoiceItem>();
        foreach (var row in rows.OrderBy(r => r.Invoice.DueDate).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Invoice.Id))
        {
            var invoice = row.Invoice;
            var figure = new InvoiceFigure(
                invoice.DueYearMonth,
                totals.GetValueOrDefault(invoice.Id),
                row.PaymentWalletId,
                paid.GetValueOrDefault(invoice.Id) ?? none);
            figures.Add(figure);

            var touchesSelection = !filtered || set.Contains(row.PaymentWalletId) || figure.Actual(set) != 0;
            if (invoice.DueYearMonth == month && touchesSelection)
            {
                lines.Add(new MonthInvoiceItem(
                    invoice.CreditCardId,
                    row.Name,
                    month,
                    invoice.ClosingDate,
                    invoice.DueDate,
                    invoice.Id,
                    InvoiceStatusRule.Evaluate(figure.TotalCents, figure.PaidCents, today, invoice.ClosingDate, invoice.DueDate),
                    figure.TotalCents,
                    figure.PaidCents,
                    figure.RemainingCents,
                    figure.Planned(set),
                    figure.Actual(set)));
            }
        }

        return new InvoiceRead(figures, lines);
    }
}
