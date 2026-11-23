using FinMy.Application.Abstractions;
using FinMy.Application.Tags;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;

namespace FinMy.Application.Entries;

/// <param name="Kind"><c>income</c> ou <c>expense</c>.</param>
/// <param name="Amount">Expressão de valor (RT-05), ex.: <c>29+73,2</c>.</param>
/// <param name="Status">Ausente → situação padrão (FR-012).</param>
/// <param name="ReferenceMonth"><c>AAAA-MM</c>; ausente → mês da data (RT-04).</param>
/// <param name="TagIds">Tags do lançamento (004 R1); ausente = nenhuma.</param>
public sealed record CreateEntryCommand(
    string? Kind,
    string? Description,
    string? Amount,
    DateOnly? Date,
    Guid? WalletId,
    string? Status,
    string? ReferenceMonth,
    string? Notes,
    IReadOnlyList<Guid>? TagIds = null);

/// <summary>Cria Entrada ou Saída (US2): valor pelo parser do servidor, situação por <c>IClock.Today</c>, carteira ativa.</summary>
public sealed class CreateEntryHandler(IEntryStore store, ITagStore tags, IClock clock)
{
    public async Task<EntryView> HandleAsync(CreateEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var kind = EntryInput.ParseKind(command.Kind);
        var amount = EntryInput.ParseAmount(command.Amount);
        var date = command.Date ?? throw new DomainException(EntryErrors.Field("date.required", "date", "Informe a data"));
        var walletId = command.WalletId is { } id && id != Guid.Empty
            ? id
            : throw new DomainException(EntryErrors.Field("wallet.required", "walletId", "Escolha a carteira"));
        EntryStatus? status = command.Status is null ? null : EntryInput.ParseStatus(command.Status);
        var referenceMonth = string.IsNullOrEmpty(command.ReferenceMonth) ? (Domain.Monetary.YearMonth?)null : EntryInput.ParseReferenceMonth(command.ReferenceMonth);

        var entry = Entry.Create(kind, command.Description!, amount, date, walletId, status, referenceMonth, command.Notes, clock.Today);

        var wallet = await store.GetWalletAsync(walletId, cancellationToken) ?? throw new DomainException(EntryErrors.WalletNotFound);
        if (wallet.IsArchived)
        {
            throw new DomainException(EntryErrors.WalletArchived);
        }

        var selection = await TagSelector.ResolveAsync(tags, command.TagIds, [], cancellationToken);
        entry.ChangeSettlementTag(selection.SettlementTagId);
        return await store.AddAsync(entry, selection.CommonTagIds, cancellationToken);
    }
}
