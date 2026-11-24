using FinMy.Application.Abstractions;
using FinMy.Application.Exports;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Tags;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Months;

/// <summary>Consultas da exportação CSV (006 research R4): uma por tipo de lançamento, no Espaço do contexto.</summary>
public sealed class EntryExportReader(FinMyDbContext db) : IEntryExportReader
{
    public async Task<IReadOnlyList<ExportRow>> ReadAsync(YearMonth firstMonth, YearMonth lastMonth, CancellationToken cancellationToken)
    {
        var firstDay = firstMonth.FirstDay;
        var lastMonthStart = lastMonth.FirstDay;
        var lastDay = lastMonth.LastDay;

        var wallets = await db.Set<Wallet>().AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);
        var tagNames = await db.Set<Tag>().AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        string WalletName(Guid id) => wallets.GetValueOrDefault(id, string.Empty);
        List<string> Names(IReadOnlyList<Guid> ids) => ids.Select(i => tagNames.GetValueOrDefault(i)).OfType<string>().ToList();

        var rows = new List<ExportRow>();

        var entries = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.ReferenceMonth >= firstDay && e.ReferenceMonth <= lastMonthStart)
            .ToListAsync(cancellationToken);
        var entryLinks = await TagLinks.ForEntriesAsync(db, entries.Select(e => e.Id).ToList(), cancellationToken);
        rows.AddRange(entries.Select(e => new ExportRow(
            e.Kind == EntryKind.Income ? ExportRowType.Income : ExportRowType.Expense,
            e.Description,
            e.EntryDate,
            e.ReferenceYearMonth,
            e.ReferenceYearMonth,
            e.Kind == EntryKind.Income ? e.AmountCents : -e.AmountCents,
            e.Status,
            WalletName(e.WalletId),
            null,
            null,
            null,
            Names(TagLinks.All(e.SettlementTagId, entryLinks, e.Id)),
            e.CreatedAt,
            e.Id)));

        var invoices = db.Set<Invoice>().AsNoTracking().Where(i => i.DueMonth >= firstDay && i.DueMonth <= lastMonthStart);
        var cards = db.Set<CreditCard>().AsNoTracking();

        var purchases = await db.Set<CardPurchase>().AsNoTracking()
            .Join(invoices, p => p.InvoiceId, i => i.Id, (p, i) => new { Purchase = p, i.DueMonth })
            .Join(cards, x => x.Purchase.CreditCardId, c => c.Id, (x, c) => new { x.Purchase, x.DueMonth, CardName = c.Name, c.PaymentWalletId })
            .ToListAsync(cancellationToken);
        var purchaseLinks = await TagLinks.ForPurchasesAsync(db, purchases.Select(p => p.Purchase.Id).ToList(), cancellationToken);
        rows.AddRange(purchases.Select(x =>
        {
            var p = x.Purchase;
            var due = YearMonth.From(x.DueMonth);
            return new ExportRow(
                ExportRowType.Purchase,
                p.Description,
                p.PurchaseDate,
                due,
                null,
                p.Nature == PurchaseNature.Credit ? p.AmountCents : -p.AmountCents,
                null,
                WalletName(x.PaymentWalletId),
                x.CardName,
                due,
                p.InstallmentNumber is { } k && p.InstallmentTotal is { } n ? $"{k}/{n}" : null,
                Names(TagLinks.All(p.SettlementTagId, purchaseLinks, p.Id)),
                p.CreatedAt,
                p.Id);
        }));

        var payments = await db.Set<InvoicePayment>().AsNoTracking()
            .Join(invoices, p => p.InvoiceId, i => i.Id, (p, i) => new { Payment = p, i.DueMonth, i.CreditCardId })
            .Join(cards, x => x.CreditCardId, c => c.Id, (x, c) => new { x.Payment, x.DueMonth, CardName = c.Name })
            .ToListAsync(cancellationToken);
        rows.AddRange(payments.Select(x =>
        {
            var due = YearMonth.From(x.DueMonth);
            return new ExportRow(
                ExportRowType.InvoicePayment,
                $"Pagamento da fatura {x.CardName}",
                x.Payment.PaymentDate,
                due,
                due,
                -x.Payment.AmountCents,
                null,
                WalletName(x.Payment.WalletId),
                x.CardName,
                due,
                null,
                [],
                x.Payment.CreatedAt,
                x.Payment.Id);
        }));

        var transfers = await db.Set<Transfer>().AsNoTracking()
            .Where(t => t.TransferDate >= firstDay && t.TransferDate <= lastDay)
            .ToListAsync(cancellationToken);
        rows.AddRange(transfers.Select(t => new ExportRow(
            ExportRowType.Transfer,
            t.Description,
            t.TransferDate,
            YearMonth.From(t.TransferDate),
            null,
            t.AmountCents,
            null,
            $"{WalletName(t.FromWalletId)} → {WalletName(t.ToWalletId)}",
            null,
            null,
            null,
            [],
            t.CreatedAt,
            t.Id)));

        return rows;
    }
}
