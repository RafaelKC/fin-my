using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Suggestions;

namespace FinMy.Application.Suggestions;

public static class SuggestionErrors
{
    public static readonly DomainError NotFound = new("suggestion.not_found", "Sugestão não encontrada.", DomainErrorKind.NotFound);
}

/// <summary>Sugestão com o cartão/carteira provável (pelo banco da notificação) para pré-preencher o formulário.</summary>
public sealed record SuggestionView(
    Guid Id,
    string Source,
    string? Bank,
    string Kind,
    string Description,
    long AmountCents,
    DateTimeOffset OccurredAt,
    string? CardLast4,
    string Status,
    Guid? SuggestedCreditCardId,
    Guid? SuggestedWalletId);

/// <summary>Dados enviados pelo celular ao ler uma notificação de banco.</summary>
public sealed record SubmitSuggestionInput(
    string? Source, string? Bank, string? Kind, string? Description, long AmountCents, DateTimeOffset? OccurredAt,
    string? CardLast4, string? RawText, string? DedupeKey);

/// <summary>Resolve cartão/carteira prováveis: cartão ativo do mesmo banco (compras) ou carteira ativa do mesmo banco.</summary>
public sealed class SuggestionTargets(ICreditCardStore cards, IWalletStore wallets, IClock clock)
{
    public async Task<Func<Suggestion, SuggestionView>> LoadAsync(CancellationToken cancellationToken)
    {
        var activeCards = (await cards.ListOverviewsAsync(CreditCardArchivedFilter.ActiveOnly, clock.Today, cancellationToken)).Select(o => o.Card).ToList();
        var activeWallets = (await wallets.ListAsync(WalletArchivedFilter.ActiveOnly, cancellationToken)).ToList();
        return s =>
        {
            Guid? card = s.Kind == "card_purchase" ? activeCards.FirstOrDefault(c => c.Bank is not null && c.Bank == s.Bank)?.Id : null;
            Guid? wallet = s.Kind != "card_purchase" ? activeWallets.FirstOrDefault(w => w.Bank is not null && w.Bank == s.Bank)?.Id : null;
            return new SuggestionView(
                s.Id, s.Source, s.Bank, s.Kind, s.Description, s.AmountCents, s.OccurredAt, s.CardLast4,
                s.Status.ToString().ToLowerInvariant(), card, wallet);
        };
    }
}

/// <summary>Celular envia uma notificação lida (token do app Android); a mesma chave devolve a sugestão já existente.</summary>
public sealed class SubmitSuggestionHandler(ISuggestionStore store, SuggestionTargets targets, IClock clock)
{
    public async Task<(SuggestionView View, bool Created)> HandleAsync(SubmitSuggestionInput input, CancellationToken cancellationToken)
    {
        var key = input.DedupeKey?.Trim() ?? string.Empty;
        var existing = key.Length == 0 ? null : await store.FindByDedupeKeyAsync(key, cancellationToken);
        var created = existing is null;
        var suggestion = existing ?? Suggestion.Create(
            input.Source ?? string.Empty, input.Bank, input.Kind ?? string.Empty, input.Description ?? string.Empty,
            input.AmountCents, input.OccurredAt ?? new DateTimeOffset(clock.Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            input.CardLast4, input.RawText ?? string.Empty, key);
        if (created)
        {
            store.Add(suggestion);
            await store.SaveAsync(cancellationToken);
        }

        return ((await targets.LoadAsync(cancellationToken))(suggestion), created);
    }
}

public sealed class ListSuggestionsHandler(ISuggestionStore store, SuggestionTargets targets)
{
    public async Task<IReadOnlyList<SuggestionView>> HandleAsync(CancellationToken cancellationToken)
    {
        var map = await targets.LoadAsync(cancellationToken);
        return (await store.ListPendingAsync(cancellationToken)).Select(map).ToList();
    }
}

public sealed class GetSuggestionHandler(ISuggestionStore store, SuggestionTargets targets)
{
    public async Task<SuggestionView> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var suggestion = await store.FindAsync(id, cancellationToken) ?? throw new DomainException(SuggestionErrors.NotFound);
        return (await targets.LoadAsync(cancellationToken))(suggestion);
    }
}

/// <summary>Descartar (<c>added = false</c>) ou marcar como lançada, guardando o lançamento criado.</summary>
public sealed class ResolveSuggestionHandler(ISuggestionStore store)
{
    public async Task HandleAsync(Guid id, bool added, Guid? linkedId, CancellationToken cancellationToken)
    {
        var suggestion = await store.FindAsync(id, cancellationToken) ?? throw new DomainException(SuggestionErrors.NotFound);
        if (added)
        {
            suggestion.MarkAdded(linkedId);
        }
        else
        {
            suggestion.Dismiss();
        }

        await store.SaveAsync(cancellationToken);
    }
}
