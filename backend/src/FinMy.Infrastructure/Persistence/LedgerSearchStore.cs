using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Tags;
using FinMy.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>
/// Busca (research R10) sobre a união de <c>entries</c>, <c>transfers</c> e <c>card_purchases</c>: filtros, total, ordem e cursor rodam no banco
/// sobre um formato comum; só os itens da página são carregados por inteiro.
/// </summary>
public sealed class LedgerSearchStore(FinMyDbContext db, ICardPurchaseStore purchaseStore) : ILedgerSearchStore
{
    private const string VersionColumn = "xmin";
    private const int EntrySource = 0;
    private const int TransferSource = 1;
    private const int PurchaseSource = 2;

    public async Task<LedgerPage> SearchAsync(LedgerSearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var all = criteria.Kinds.Count == 0;
        var wantIncome = all || criteria.Kinds.Contains(LedgerItemKind.Income);
        var wantExpense = all || criteria.Kinds.Contains(LedgerItemKind.Expense);
        var tagIds = criteria.TagIds.ToList();
        var wantTransfer = (all || criteria.Kinds.Contains(LedgerItemKind.Transfer)) && criteria.Status is null && tagIds.Count == 0;

        // Compra no cartão não tem carteira nem situação: esses filtros a deixam de fora (004 research R6).
        var wantPurchase = (all || criteria.Kinds.Contains(LedgerItemKind.CardPurchase)) && criteria.Status is null && criteria.WalletIds.Count == 0;

        var pattern = criteria.NormalizedText is { } text ? $"%{EntryStore.EscapeLike(text)}%" : null;
        var wallets = criteria.WalletIds.ToList();

        var entries = db.Set<Entry>().AsNoTracking().Where(e => (wantIncome && e.Kind == EntryKind.Income) || (wantExpense && e.Kind == EntryKind.Expense));
        if (pattern is not null)
        {
            entries = entries.Where(e => EF.Functions.Like(e.DescriptionNormalized, pattern, "\\"));
        }

        if (wallets.Count > 0)
        {
            entries = entries.Where(e => wallets.Contains(e.WalletId));
        }

        if (tagIds.Count > 0)
        {
            entries = entries.Where(e =>
                (e.SettlementTagId != null && tagIds.Contains(e.SettlementTagId.Value))
                || db.Set<EntryTag>().Any(l => l.EntryId == e.Id && tagIds.Contains(l.TagId)));
        }

        if (criteria.Status is { } status)
        {
            entries = entries.Where(e => e.Status == status);
        }

        if (criteria.From is { } from)
        {
            entries = entries.Where(e => e.EntryDate >= from);
        }

        if (criteria.To is { } to)
        {
            entries = entries.Where(e => e.EntryDate <= to);
        }

        var rows = entries.Select(e => new Row
        {
            Source = EntrySource,
            Id = e.Id,
            Date = e.EntryDate,
            CreatedAt = e.CreatedAt,
            SignedCents = e.Kind == EntryKind.Income ? e.AmountCents : -e.AmountCents,
        });

        if (wantTransfer)
        {
            var transfers = db.Set<Transfer>().AsNoTracking();
            if (pattern is not null)
            {
                transfers = transfers.Where(t => EF.Functions.Like(t.DescriptionNormalized, pattern, "\\"));
            }

            if (wallets.Count > 0)
            {
                transfers = transfers.Where(t => wallets.Contains(t.FromWalletId) || wallets.Contains(t.ToWalletId));
            }

            if (criteria.From is { } tFrom)
            {
                transfers = transfers.Where(t => t.TransferDate >= tFrom);
            }

            if (criteria.To is { } tTo)
            {
                transfers = transfers.Where(t => t.TransferDate <= tTo);
            }

            rows = rows.Concat(transfers.Select(t => new Row
            {
                Source = TransferSource,
                Id = t.Id,
                Date = t.TransferDate,
                CreatedAt = t.CreatedAt,
                SignedCents = 0,
            }));
        }

        if (wantPurchase)
        {
            var purchases = db.Set<CardPurchase>().AsNoTracking();
            if (pattern is not null)
            {
                purchases = purchases.Where(p => EF.Functions.Like(p.DescriptionNormalized, pattern, "\\"));
            }

            if (criteria.From is { } pFrom)
            {
                purchases = purchases.Where(p => p.PurchaseDate >= pFrom);
            }

            if (criteria.To is { } pTo)
            {
                purchases = purchases.Where(p => p.PurchaseDate <= pTo);
            }

            if (tagIds.Count > 0)
            {
                purchases = purchases.Where(p =>
                    (p.SettlementTagId != null && tagIds.Contains(p.SettlementTagId.Value))
                    || db.Set<CardPurchaseTag>().Any(l => l.CardPurchaseId == p.Id && tagIds.Contains(l.TagId)));
            }

            rows = rows.Concat(purchases.Select(p => new Row
            {
                Source = PurchaseSource,
                Id = p.Id,
                Date = p.PurchaseDate,
                CreatedAt = p.CreatedAt,
                SignedCents = p.Nature == PurchaseNature.Credit ? p.AmountCents : -p.AmountCents,
            }));
        }
        else if (!wantTransfer && !wantIncome && !wantExpense)
        {
            return new LedgerPage([], 0, 0, null);
        }

        var totalCount = await rows.CountAsync(cancellationToken);
        var totalCents = totalCount == 0 ? 0 : await rows.SumAsync(r => r.SignedCents, cancellationToken);

        var page = rows;
        if (criteria.After is { } after)
        {
            var afterAt = new DateTimeOffset(after.CreatedAtUtcTicks, TimeSpan.Zero);
            page = page.Where(r =>
                r.Date < after.Date
                || (r.Date == after.Date && r.CreatedAt < afterAt)
                || (r.Date == after.Date && r.CreatedAt == afterAt && r.Id.CompareTo(after.Id) < 0));
        }

        var ordered = await page
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(criteria.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = ordered.Count > criteria.Limit;
        var pageRows = hasMore ? ordered.Take(criteria.Limit).ToList() : ordered;

        var entryViews = await LoadEntriesAsync(IdsOf(pageRows, EntrySource), cancellationToken);
        var transferViews = await LoadTransfersAsync(IdsOf(pageRows, TransferSource), cancellationToken);
        var purchaseViews = await LoadPurchasesAsync(IdsOf(pageRows, PurchaseSource), cancellationToken);

        var items = pageRows
            .Select(r => r.Source switch
            {
                TransferSource => new LedgerItemView(LedgerItemKind.Transfer, null, transferViews[r.Id]),
                PurchaseSource => new LedgerItemView(LedgerItemKind.CardPurchase, null, null, purchaseViews[r.Id]),
                _ => new LedgerItemView(entryViews[r.Id].Kind == EntryKind.Income ? LedgerItemKind.Income : LedgerItemKind.Expense, entryViews[r.Id], null),
            })
            .ToList();

        LedgerCursor? next = null;
        if (hasMore)
        {
            var last = pageRows[^1];
            next = new LedgerCursor(last.Date, last.CreatedAt.UtcTicks, last.Id);
        }

        return new LedgerPage(items, totalCents, totalCount, next);
    }

    private async Task<Dictionary<Guid, EntryView>> LoadEntriesAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var loaded = await db.Set<Entry>().AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { Entry = e, Version = EF.Property<uint>(e, VersionColumn) })
            .ToListAsync(cancellationToken);

        var links = await TagLinks.ForEntriesAsync(db, ids, cancellationToken);
        var counts = await SplitCounts.ForEntriesAsync(db, loaded.Select(x => x.Entry.SplitGroupId), cancellationToken);
        return loaded.ToDictionary(
            x => x.Entry.Id,
            x => new EntryView(
                x.Entry.Id, x.Entry.Kind, x.Entry.Description, x.Entry.AmountCents, x.Entry.EntryDate, x.Entry.ReferenceYearMonth,
                x.Entry.Status, x.Entry.WalletId, x.Entry.Notes, x.Entry.CreatedAt, x.Entry.CreatedVia, x.Version,
                TagLinks.All(x.Entry.SettlementTagId, links, x.Entry.Id), x.Entry.SettlementTagId,
                x.Entry.SplitGroupId, SplitCounts.Of(x.Entry.SplitGroupId, counts),
                x.Entry.RecurrenceId, x.Entry.RecurrenceMonth is { } month ? YearMonth.From(month) : null, x.Entry.RecurrenceModified));
    }

    private async Task<Dictionary<Guid, CardPurchaseView>> LoadPurchasesAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var views = await purchaseStore.GetViewsAsync(ids, cancellationToken);
        return views.ToDictionary(v => v.Id);
    }

    private static List<Guid> IdsOf(List<Row> rows, int source) => rows.Where(r => r.Source == source).Select(r => r.Id).ToList();

    private async Task<Dictionary<Guid, TransferView>> LoadTransfersAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var loaded = await db.Set<Transfer>().AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { Transfer = t, Version = EF.Property<uint>(t, VersionColumn) })
            .ToListAsync(cancellationToken);

        return loaded.ToDictionary(
            x => x.Transfer.Id,
            x => new TransferView(
                x.Transfer.Id, x.Transfer.FromWalletId, x.Transfer.ToWalletId, x.Transfer.AmountCents, x.Transfer.TransferDate,
                x.Transfer.Description, x.Transfer.Notes, x.Transfer.CreatedAt, x.Transfer.CreatedVia, x.Version));
    }

    /// <summary>Formato comum de Entrada/Saída e Transferência para a união (só o necessário para filtrar, ordenar e somar).</summary>
    private sealed class Row
    {
        public int Source { get; set; }

        public Guid Id { get; set; }

        public DateOnly Date { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public long SignedCents { get; set; }
    }
}
