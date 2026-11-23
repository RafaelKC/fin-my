using FinMy.Application.Abstractions;
using FinMy.Domain.Entries;
using FinMy.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>
/// Busca (research R10) sobre a união de <c>entries</c> e <c>transfers</c>: filtros, total, ordem e cursor rodam no banco
/// sobre um formato comum; só os itens da página são carregados por inteiro.
/// </summary>
public sealed class LedgerSearchStore(FinMyDbContext db) : ILedgerSearchStore
{
    private const string VersionColumn = "xmin";

    public async Task<LedgerPage> SearchAsync(LedgerSearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var all = criteria.Kinds.Count == 0;
        var wantIncome = all || criteria.Kinds.Contains(LedgerItemKind.Income);
        var wantExpense = all || criteria.Kinds.Contains(LedgerItemKind.Expense);
        var wantTransfer = (all || criteria.Kinds.Contains(LedgerItemKind.Transfer)) && criteria.Status is null;

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
            IsTransfer = false,
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
                IsTransfer = true,
                Id = t.Id,
                Date = t.TransferDate,
                CreatedAt = t.CreatedAt,
                SignedCents = 0,
            }));
        }
        else if (!wantIncome && !wantExpense)
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

        var entryIds = pageRows.Where(r => !r.IsTransfer).Select(r => r.Id).ToList();
        var transferIds = pageRows.Where(r => r.IsTransfer).Select(r => r.Id).ToList();

        var entryViews = await LoadEntriesAsync(entryIds, cancellationToken);
        var transferViews = await LoadTransfersAsync(transferIds, cancellationToken);

        var items = pageRows
            .Select(r => r.IsTransfer
                ? new LedgerItemView(LedgerItemKind.Transfer, null, transferViews[r.Id])
                : new LedgerItemView(entryViews[r.Id].Kind == EntryKind.Income ? LedgerItemKind.Income : LedgerItemKind.Expense, entryViews[r.Id], null))
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

        return loaded.ToDictionary(
            x => x.Entry.Id,
            x => new EntryView(
                x.Entry.Id, x.Entry.Kind, x.Entry.Description, x.Entry.AmountCents, x.Entry.EntryDate, x.Entry.ReferenceYearMonth,
                x.Entry.Status, x.Entry.WalletId, x.Entry.Notes, x.Entry.CreatedAt, x.Version));
    }

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
                x.Transfer.Description, x.Transfer.Notes, x.Transfer.CreatedAt, x.Version));
    }

    /// <summary>Formato comum de Entrada/Saída e Transferência para a união (só o necessário para filtrar, ordenar e somar).</summary>
    private sealed class Row
    {
        public bool IsTransfer { get; set; }

        public Guid Id { get; set; }

        public DateOnly Date { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public long SignedCents { get; set; }
    }
}
