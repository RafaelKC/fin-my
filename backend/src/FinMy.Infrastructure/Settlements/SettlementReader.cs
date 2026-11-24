using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Settlements;
using FinMy.Domain.Tags;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Settlements;

/// <summary>
/// Consultas dos acertos (research R3): somas agregadas no banco por tag e mês (Saídas e Entradas pelo Mês de
/// referência; compras pelo vencimento da fatura) e a lista dos lançamentos de um mês. Nada fica desnormalizado.
/// </summary>
public sealed class SettlementReader(FinMyDbContext db) : ISettlementReader
{
    public async Task<IReadOnlyList<SettlementTagData>> ReadAsync(YearMonth month, CancellationToken cancellationToken)
    {
        var first = month.FirstDay;

        var tags = await db.Set<Tag>().AsNoTracking()
            .Where(t => t.IsSettlement)
            .Select(t => new { t.Id, t.Name, t.Color, Archived = t.ArchivedAt != null })
            .ToListAsync(cancellationToken);

        var entryRows = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.SettlementTagId != null && e.ReferenceMonth <= first)
            .GroupBy(e => new { TagId = e.SettlementTagId!.Value, e.ReferenceMonth, e.Kind, e.Status })
            .Select(g => new { g.Key.TagId, g.Key.ReferenceMonth, g.Key.Kind, g.Key.Status, Cents = g.Sum(e => e.AmountCents) })
            .ToListAsync(cancellationToken);

        var purchaseRows = await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => p.SettlementTagId != null)
            .Join(db.Set<Invoice>().AsNoTracking().Where(i => i.DueMonth <= first), p => p.InvoiceId, i => i.Id, (p, i) => new { p, i.DueMonth })
            .GroupBy(r => new { TagId = r.p.SettlementTagId!.Value, r.DueMonth, r.p.Nature })
            .Select(g => new { g.Key.TagId, g.Key.DueMonth, g.Key.Nature, Cents = g.Sum(r => r.p.AmountCents) })
            .ToListAsync(cancellationToken);

        var sums = new Dictionary<(Guid TagId, YearMonth Month), Sums>();
        Sums For(Guid tagId, DateOnly date) => sums.TryGetValue((tagId, YearMonth.From(date)), out var s)
            ? s
            : sums[(tagId, YearMonth.From(date))] = new Sums();

        foreach (var r in entryRows)
        {
            var s = For(r.TagId, r.ReferenceMonth);
            if (r.Kind == EntryKind.Expense)
            {
                s.Expense += r.Cents;
            }
            else
            {
                s.Charged += r.Cents;
                if (r.Status == EntryStatus.Settled)
                {
                    s.Received += r.Cents;
                }
            }
        }

        foreach (var r in purchaseRows)
        {
            var s = For(r.TagId, r.DueMonth);
            if (r.Nature == PurchaseNature.Credit)
            {
                s.CardCredit += r.Cents;
            }
            else
            {
                s.CardDebit += r.Cents;
            }
        }

        return tags
            .Select(t => new SettlementTagData(
                t.Id,
                t.Name,
                t.Color,
                t.Archived,
                sums.Where(kv => kv.Key.TagId == t.Id)
                    .ToDictionary(kv => kv.Key.Month, kv => new SettlementMonthInput(
                        kv.Value.Expense, kv.Value.CardDebit, kv.Value.CardCredit, kv.Value.Charged, kv.Value.Received))))
            .ToList();
    }

    public async Task<IReadOnlyList<SettlementItem>> ReadItemsAsync(Guid tagId, YearMonth month, CancellationToken cancellationToken)
    {
        var first = month.FirstDay;

        var entries = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.SettlementTagId == tagId && e.ReferenceMonth == first)
            .Select(e => new { e.Id, e.Kind, e.Description, e.EntryDate, e.AmountCents, e.Status, e.CreatedAt })
            .ToListAsync(cancellationToken);

        var purchases = await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => p.SettlementTagId == tagId)
            .Join(db.Set<Invoice>().AsNoTracking().Where(i => i.DueMonth == first), p => p.InvoiceId, i => i.Id, (p, i) => p)
            .ToListAsync(cancellationToken);

        var items = new List<(SettlementItem Item, DateTimeOffset CreatedAt)>();
        foreach (var e in entries)
        {
            items.Add(e.Kind == EntryKind.Income
                ? (new SettlementItem(SettlementGroup.Charged, "income", e.Id, e.Description, e.EntryDate, e.AmountCents, e.Status == EntryStatus.Settled, null), e.CreatedAt)
                : (new SettlementItem(SettlementGroup.Spent, "expense", e.Id, e.Description, e.EntryDate, e.AmountCents, null, null), e.CreatedAt));
        }

        foreach (var p in purchases)
        {
            var signed = p.Nature == PurchaseNature.Credit ? -p.AmountCents : p.AmountCents;
            items.Add((new SettlementItem(SettlementGroup.Spent, "card_purchase", p.Id, p.DisplayDescription, p.PurchaseDate, signed, null, p.CreditCardId), p.CreatedAt));
        }

        return items
            .OrderBy(i => i.Item.Group)
            .ThenBy(i => i.Item.Date)
            .ThenBy(i => i.CreatedAt)
            .ThenBy(i => i.Item.Id)
            .Select(i => i.Item)
            .ToList();
    }

    private sealed class Sums
    {
        public long Expense { get; set; }

        public long CardDebit { get; set; }

        public long CardCredit { get; set; }

        public long Charged { get; set; }

        public long Received { get; set; }
    }
}
