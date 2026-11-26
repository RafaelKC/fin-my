using FinMy.Application.Abstractions;
using FinMy.Application.Catalog;
using FinMy.Application.Entries;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;

namespace FinMy.Application.Widget;

/// <summary>Lançamento rápido criado pelo relógio (010): o que foi gravado, com o nome da carteira escolhida pelo padrão.</summary>
public sealed record WidgetCreatedEntry(Guid Id, string Kind, string Description, long AmountCents, DateOnly Date, string Status, string WalletName);

/// <summary>
/// Lançamento rápido do relógio (010): só tipo, descrição e valor; data = hoje, carteira e situação pelos mesmos padrões do
/// Claude (<see cref="CreationDefaults"/>, regra da situação do app). Gravado com o canal do app Android.
/// </summary>
public sealed class CreateWidgetEntryHandler(CreationDefaults defaults, CreateEntryHandler create, IWalletStore wallets, IClock clock)
{
    public async Task<WidgetCreatedEntry> HandleAsync(string? kind, string? description, string? amount, CancellationToken cancellationToken)
    {
        var parsedKind = EntryInput.ParseKind(kind);
        var wallet = await defaults.WalletAsync(parsedKind, description, cancellationToken);
        var entry = await create.HandleAsync(
            new CreateEntryCommand(kind, description, amount, clock.Today, wallet.Id, null, null, null), cancellationToken);
        var walletName = (await wallets.ListAsync(WalletArchivedFilter.All, cancellationToken)).FirstOrDefault(w => w.Id == entry.WalletId)?.Name ?? string.Empty;
        return new WidgetCreatedEntry(
            entry.Id,
            entry.Kind == EntryKind.Income ? "income" : "expense",
            entry.Description,
            entry.AmountCents,
            entry.Date,
            entry.Status == EntryStatus.Settled ? "settled" : "planned",
            walletName);
    }
}

/// <summary>Marca uma prevista como recebida/paga pelo relógio (010). Só <c>settled</c>: o relógio não desfaz nem edita.</summary>
public sealed class SettleWidgetEntryHandler(ChangeEntryStatusHandler change)
{
    public async Task HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await change.HandleAsync(id, "settled", cancellationToken);
}
