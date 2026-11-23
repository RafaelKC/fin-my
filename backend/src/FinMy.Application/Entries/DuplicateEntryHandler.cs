using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Entries;

/// <summary>
/// Duplica um lançamento com nova data (FR-024): tipo, descrição, valor, carteira e observação se repetem; o Mês de referência
/// e a Situação são recalculados como na criação; as tags (menos as arquivadas) também se repetem. A carteira original precisa estar ativa.
/// </summary>
public sealed class DuplicateEntryHandler(IEntryStore store, ITagStore tagStore, IClock clock)
{
    public async Task<EntryView> HandleAsync(Guid id, DateOnly? date, CancellationToken cancellationToken)
    {
        var source = await store.GetAsync(id, cancellationToken) ?? throw new DomainException(EntryErrors.NotFound);
        var newDate = date ?? throw new DomainException(EntryErrors.Field("date.required", "date", "Informe a data"));

        var copy = Entry.Create(
            source.Kind, source.Description, Money.FromCents(source.AmountCents), newDate, source.WalletId, null, null, source.Notes, clock.Today);

        var wallet = await store.GetWalletAsync(source.WalletId, cancellationToken) ?? throw new DomainException(EntryErrors.WalletNotFound);
        if (wallet.IsArchived)
        {
            throw new DomainException(EntryErrors.WalletArchived);
        }

        // A cópia leva as tags do original (as arquivadas ficam de fora: tag arquivada só se mantém, nunca se adiciona).
        var tags = await tagStore.FindManyAsync(source.TagIds, cancellationToken);
        var active = tags.Where(t => !t.IsArchived).ToList();
        copy.ChangeSettlementTag(active.SingleOrDefault(t => t.IsSettlement)?.Id);
        return await store.AddAsync(copy, active.Where(t => !t.IsSettlement).Select(t => t.Id).ToList(), cancellationToken);
    }
}
