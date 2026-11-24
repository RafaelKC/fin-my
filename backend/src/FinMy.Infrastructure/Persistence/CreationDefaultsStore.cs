using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>Padrões de criação do Claude (008 FR-005), lidos do Espaço do contexto.</summary>
public sealed class CreationDefaultsStore(FinMyDbContext db) : ICreationDefaultsStore
{
    public async Task<Guid?> LatestWalletAsync(EntryKind kind, string normalizedDescription, CancellationToken cancellationToken)
    {
        var walletId = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.Kind == kind && e.DescriptionNormalized == normalizedDescription)
            .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.CreatedAt)
            .Select(e => (Guid?)e.WalletId)
            .FirstOrDefaultAsync(cancellationToken);

        return walletId is { } id && await db.Set<Wallet>().AsNoTracking().AnyAsync(w => w.Id == id && w.ArchivedAt == null, cancellationToken)
            ? walletId
            : null;
    }

    public async Task<Guid?> LatestCreditCardAsync(string normalizedDescription, CancellationToken cancellationToken)
    {
        var cardId = await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => p.DescriptionNormalized == normalizedDescription)
            .OrderByDescending(p => p.PurchaseDate).ThenByDescending(p => p.CreatedAt)
            .Select(p => (Guid?)p.CreditCardId)
            .FirstOrDefaultAsync(cancellationToken);

        return cardId is { } id && await db.Set<CreditCard>().AsNoTracking().AnyAsync(c => c.Id == id && c.ArchivedAt == null, cancellationToken)
            ? cardId
            : null;
    }

    public async Task<Guid?> MostUsedCreditCardAsync(DateOnly since, CancellationToken cancellationToken)
    {
        var active = db.Set<CreditCard>().AsNoTracking().Where(c => c.ArchivedAt == null).Select(c => c.Id);
        return await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => p.PurchaseDate >= since && active.Contains(p.CreditCardId))
            .GroupBy(p => p.CreditCardId)
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(p => p.CreatedAt))
            .Select(g => (Guid?)g.Key)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
