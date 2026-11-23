using FinMy.Application.Abstractions;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>Grava transferências no Espaço do contexto; o filtro de Espaço e o de exclusão lógica já valem nas consultas.</summary>
public sealed class TransferStore(FinMyDbContext db) : ITransferStore
{
    public async Task<WalletAvailability> GetWalletAvailabilityAsync(Guid walletId, CancellationToken cancellationToken)
    {
        var archivedAt = await db.Set<Wallet>().AsNoTracking()
            .Where(w => w.Id == walletId)
            .Select(w => new { w.ArchivedAt })
            .SingleOrDefaultAsync(cancellationToken);

        return archivedAt is null
            ? WalletAvailability.NotFound
            : archivedAt.ArchivedAt is null ? WalletAvailability.Active : WalletAvailability.Archived;
    }

    public async Task<TransferView> AddAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        db.Add(transfer);
        await db.SaveChangesAsync(cancellationToken);
        return ToView(transfer);
    }

    public async Task<TransferView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        // Sem rastreamento o xmin (propriedade sombra) não é materializado; projeta junto.
        var row = await db.Set<Transfer>().AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new { Transfer = t, Version = EF.Property<uint>(t, "xmin") })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToView(row.Transfer, row.Version);
    }

    public async Task<(TransferUpdateOutcome Outcome, TransferView? Transfer)> UpdateAsync(
        Guid id, long version, Action<Transfer> apply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);

        var transfer = await db.Set<Transfer>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (transfer is null)
        {
            return (TransferUpdateOutcome.NotFound, null);
        }

        var xmin = db.Entry(transfer).Property<uint>("xmin");
        if (xmin.CurrentValue != version)
        {
            return (TransferUpdateOutcome.VersionConflict, null);
        }

        apply(transfer);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outro aparelho gravou entre a leitura e a gravação.
            db.ChangeTracker.Clear();
            return (TransferUpdateOutcome.VersionConflict, null);
        }

        return (TransferUpdateOutcome.Updated, ToView(transfer));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var transfer = await db.Set<Transfer>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (transfer is null)
        {
            return false;
        }

        db.Remove(transfer);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private long VersionOf(Transfer transfer) => db.Entry(transfer).Property<uint>("xmin").CurrentValue;

    private TransferView ToView(Transfer transfer) => ToView(transfer, VersionOf(transfer));

    private static TransferView ToView(Transfer t, long version) => new(
        t.Id, t.FromWalletId, t.ToWalletId, t.AmountCents, t.TransferDate, t.Description, t.Notes, ToMicroseconds(t.CreatedAt), version);

    // O PostgreSQL guarda microssegundos: sem truncar, o POST devolveria mais casas do que um GET depois.
    private static DateTimeOffset ToMicroseconds(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));
}
