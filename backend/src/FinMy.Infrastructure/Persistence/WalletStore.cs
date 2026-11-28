using FinMy.Application.Abstractions;
using FinMy.Application.Wallets;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace FinMy.Infrastructure.Persistence;

/// <summary>Carteiras do Espaço do contexto; saldo atual e "tem lançamentos" vêm de consultas agregadas (research R5).</summary>
public sealed class WalletStore(FinMyDbContext db) : IWalletStore
{
    private const string VersionColumn = "xmin";

    public Task<Wallet?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Wallet>().SingleOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<Wallet?> FindPrimaryAsync(CancellationToken cancellationToken) =>
        db.Set<Wallet>().SingleOrDefaultAsync(w => w.IsPrimary && w.ArchivedAt == null, cancellationToken);

    public Task<bool> AnyActiveAsync(Guid? exceptId, CancellationToken cancellationToken) =>
        db.Set<Wallet>().AnyAsync(w => w.ArchivedAt == null && (exceptId == null || w.Id != exceptId), cancellationToken);

    public async Task<bool> HasEntriesAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<Entry>().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).AnyAsync(e => e.WalletId == id, cancellationToken)
        || await db.Set<Transfer>().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AnyAsync(t => t.FromWalletId == id || t.ToWalletId == id, cancellationToken)
        || await db.Set<InvoicePayment>().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AnyAsync(p => p.WalletId == id, cancellationToken);

    public async Task<WalletView?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await ViewsAsync(db.Set<Wallet>().Where(w => w.Id == id), cancellationToken)).SingleOrDefault();

    public Task<IReadOnlyList<WalletView>> ListAsync(WalletArchivedFilter filter, CancellationToken cancellationToken)
    {
        var query = db.Set<Wallet>().AsQueryable();
        query = filter switch
        {
            WalletArchivedFilter.ActiveOnly => query.Where(w => w.ArchivedAt == null),
            WalletArchivedFilter.ArchivedOnly => query.Where(w => w.ArchivedAt != null),
            _ => query,
        };

        return ViewsAsync(query, cancellationToken);
    }

    public long VersionOf(Wallet wallet) => db.Entry(wallet).Property<uint>(VersionColumn).CurrentValue;

    public void ExpectVersion(Wallet wallet, long version) =>
        db.Entry(wallet).Property<uint>(VersionColumn).OriginalValue = (uint)version;

    public void Add(Wallet wallet) => db.Set<Wallet>().Add(wallet);

    public void Remove(Wallet wallet) => db.Set<Wallet>().Remove(wallet);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // Nome repetido é o erro esperado; a corrida de duas "primeiras carteiras" é um conflito de concorrência.
            throw new DomainException(pg.ConstraintName?.Contains("primary", StringComparison.OrdinalIgnoreCase) == true
                ? WalletErrors.ConcurrencyConflict
                : WalletErrors.NameTaken);
        }
    }

    public async Task<IWalletTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new WalletTransaction(await db.Database.BeginTransactionAsync(cancellationToken));

    private async Task<IReadOnlyList<WalletView>> ViewsAsync(IQueryable<Wallet> wallets, CancellationToken cancellationToken)
    {
        var rows = await wallets.AsNoTracking()
            .OrderByDescending(w => w.IsPrimary).ThenBy(w => w.NameNormalized)
            .Select(w => new { Wallet = w, Version = EF.Property<uint>(w, VersionColumn) })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Wallet.Id).ToList();

        // Saldo atual (FR-003): entradas recebidas − saídas pagas + transferências recebidas − enviadas − pagamentos de fatura, sem filtro de data.
        var settled = await db.Set<Entry>().AsNoTracking()
            .Where(e => ids.Contains(e.WalletId) && e.Status == EntryStatus.Settled)
            .GroupBy(e => new { e.WalletId, e.Kind })
            .Select(g => new { g.Key.WalletId, g.Key.Kind, Total = g.Sum(e => e.AmountCents) })
            .ToListAsync(cancellationToken);
        var received = await db.Set<Transfer>().AsNoTracking()
            .Where(t => ids.Contains(t.ToWalletId))
            .GroupBy(t => t.ToWalletId)
            .Select(g => new { WalletId = g.Key, Total = g.Sum(t => t.AmountCents) })
            .ToListAsync(cancellationToken);
        var sent = await db.Set<Transfer>().AsNoTracking()
            .Where(t => ids.Contains(t.FromWalletId))
            .GroupBy(t => t.FromWalletId)
            .Select(g => new { WalletId = g.Key, Total = g.Sum(t => t.AmountCents) })
            .ToListAsync(cancellationToken);

        // Pagamentos de fatura saem da carteira usada (003 FR-024).
        var invoicePaid = await db.Set<InvoicePayment>().AsNoTracking()
            .Where(p => ids.Contains(p.WalletId))
            .GroupBy(p => p.WalletId)
            .Select(g => new { WalletId = g.Key, Total = g.Sum(p => p.AmountCents) })
            .ToListAsync(cancellationToken);

        // "Tem lançamentos" conta também as linhas excluídas e ainda não expurgadas (research R6).
        var withEntries = (await db.Set<Entry>().AsNoTracking().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Where(e => ids.Contains(e.WalletId)).Select(e => e.WalletId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        withEntries.UnionWith(await db.Set<Transfer>().AsNoTracking().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Where(t => ids.Contains(t.FromWalletId)).Select(t => t.FromWalletId).Distinct().ToListAsync(cancellationToken));
        withEntries.UnionWith(await db.Set<Transfer>().AsNoTracking().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Where(t => ids.Contains(t.ToWalletId)).Select(t => t.ToWalletId).Distinct().ToListAsync(cancellationToken));
        withEntries.UnionWith(await db.Set<InvoicePayment>().AsNoTracking().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Where(p => ids.Contains(p.WalletId)).Select(p => p.WalletId).Distinct().ToListAsync(cancellationToken));

        return rows.Select(r =>
        {
            var w = r.Wallet;
            var balance = w.InitialBalanceCents
                + settled.Where(s => s.WalletId == w.Id).Sum(s => s.Kind == EntryKind.Income ? s.Total : -s.Total)
                + received.Where(t => t.WalletId == w.Id).Sum(t => t.Total)
                - sent.Where(t => t.WalletId == w.Id).Sum(t => t.Total)
                - invoicePaid.Where(p => p.WalletId == w.Id).Sum(p => p.Total);

            return new WalletView(
                w.Id, w.Name, w.Type, w.Color, w.InitialBalanceCents, w.InitialBalanceDate,
                w.IsPrimary, w.IsArchived, balance, withEntries.Contains(w.Id), r.Version, w.Bank);
        }).ToList();
    }

    private sealed class WalletTransaction(IDbContextTransaction transaction) : IWalletTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
