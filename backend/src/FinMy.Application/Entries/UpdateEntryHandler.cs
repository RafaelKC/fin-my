using FinMy.Application.Abstractions;
using FinMy.Application.Tags;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Entries;

/// <param name="TagIds">Substitui as tags do lançamento (004 R1); ausente = não mexe nas tags.</param>
/// <param name="Version">Obrigatória; divergente da atual → conflito de concorrência (research R11).</param>
/// <param name="ReferenceMonth">Ausente ou igual ao atual → o mês acompanha a data só se nunca foi alterado (RT-04, research R7).</param>
public sealed record UpdateEntryCommand(
    Guid Id,
    string? Kind,
    string? Description,
    string? Amount,
    DateOnly? Date,
    Guid? WalletId,
    string? Status,
    string? ReferenceMonth,
    string? Notes,
    long? Version,
    IReadOnlyList<Guid>? TagIds = null);

/// <summary>Edita todos os campos de um lançamento (FR-022). A carteira só precisa estar ativa ao ser trocada (research R7).</summary>
public sealed class UpdateEntryHandler(IEntryStore store, ITagStore tags)
{
    public async Task<EntryView> HandleAsync(UpdateEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var current = await store.GetAsync(command.Id, cancellationToken) ?? throw new DomainException(EntryErrors.NotFound);

        var kind = EntryInput.ParseKind(command.Kind);
        var amount = EntryInput.ParseAmount(command.Amount);
        var date = command.Date ?? throw new DomainException(EntryErrors.Field("date.required", "date", "Informe a data"));
        var walletId = command.WalletId is { } id && id != Guid.Empty
            ? id
            : throw new DomainException(EntryErrors.Field("wallet.required", "walletId", "Escolha a carteira"));
        var status = EntryInput.ParseStatus(command.Status);
        YearMonth? referenceMonth = string.IsNullOrEmpty(command.ReferenceMonth) ? null : EntryInput.ParseReferenceMonth(command.ReferenceMonth);
        var version = command.Version ?? throw new DomainException(EntryErrors.Field("version.required", "version", "Informe a versão do lançamento"));

        if (walletId != current.WalletId)
        {
            var wallet = await store.GetWalletAsync(walletId, cancellationToken) ?? throw new DomainException(EntryErrors.WalletNotFound);
            if (wallet.IsArchived)
            {
                throw new DomainException(EntryErrors.WalletArchived);
            }
        }

        var selection = command.TagIds is null
            ? null
            : await TagSelector.ResolveAsync(tags, command.TagIds, current.TagIds, cancellationToken);

        var updated = await store.UpdateAsync(
            command.Id,
            version,
            entry =>
            {
                var monthBefore = entry.ReferenceYearMonth;
                var contentBefore = entry.RecurrenceContent;
                entry.ChangeKind(kind);
                entry.ChangeDescription(command.Description!);
                entry.ChangeAmount(amount);
                entry.ChangeWallet(walletId);
                entry.ChangeStatus(status);
                entry.ChangeNotes(command.Notes);
                entry.ChangeDate(date); // acompanha o mês se ele nunca foi alterado à mão (RT-04)
                if (referenceMonth is { } month && month != monthBefore)
                {
                    entry.ChangeReferenceMonth(month); // o cliente pediu outro mês de propósito
                }

                if (selection is not null)
                {
                    entry.ChangeSettlementTag(selection.SettlementTagId);
                }

                // Qualquer mudança além de situação marca a ocorrência como "alterada" (005 FR-005, research R3).
                if (entry.RecurrenceContent != contentBefore || TagsChanged(current.TagIds, selection))
                {
                    entry.MarkRecurrenceModified();
                }
            },
            selection?.CommonTagIds,
            cancellationToken);

        return updated ?? throw new DomainException(EntryErrors.NotFound);
    }

    /// <summary>As tags pedidas (a de acerto e as comuns) diferem das que o lançamento já tinha?</summary>
    internal static bool TagsChanged(IReadOnlyCollection<Guid> currentTagIds, TagSelection? selection) =>
        selection is not null
        && !currentTagIds.ToHashSet().SetEquals(selection.SettlementTagId is { } s ? [s, .. selection.CommonTagIds] : selection.CommonTagIds);
}
