using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Catalog;

/// <summary>Valor escolhido por padrão e de onde veio (<see cref="CreationDefaults"/>): vai na resposta da criação (008 FR-005).</summary>
public sealed record DefaultChoice(Guid Id, string Source);

/// <summary>
/// Padrões de carteira e cartão quando a pessoa não os diz (008 FR-005, research R4): o do lançamento mais recente com a mesma
/// descrição (e tipo); senão a Carteira principal, ou o cartão com mais compras nos últimos 30 dias. Sem cartão possível →
/// <c>card_required</c> com as opções.
/// </summary>
public sealed class CreationDefaults(ICreationDefaultsStore store, CatalogResolver catalog, IClock clock)
{
    public const string CardRequiredCode = "card_required";
    public const string SameDescription = "same_description";
    public const string PrimaryWallet = "primary_wallet";
    public const string MostUsedCard = "most_used_card_30d";
    public const int MostUsedDays = 30;

    public async Task<DefaultChoice> WalletAsync(EntryKind kind, string? description, CancellationToken cancellationToken)
    {
        var normalized = TextNormalizer.Normalize(description ?? string.Empty);
        if (normalized.Length > 0 && await store.LatestWalletAsync(kind, normalized, cancellationToken) is { } recent)
        {
            return new DefaultChoice(recent, SameDescription);
        }

        var view = await catalog.GetAsync(cancellationToken);
        var primary = view.Wallets.FirstOrDefault(w => w.IsPrimary);
        // Sem carteira principal ativa, a resolução sem nome devolve o erro com as opções.
        return new DefaultChoice(primary?.Id ?? await catalog.ResolveWalletAsync(null, "wallet", cancellationToken), PrimaryWallet);
    }

    public async Task<DefaultChoice> CreditCardAsync(string? description, CancellationToken cancellationToken)
    {
        var normalized = TextNormalizer.Normalize(description ?? string.Empty);
        if (normalized.Length > 0 && await store.LatestCreditCardAsync(normalized, cancellationToken) is { } recent)
        {
            return new DefaultChoice(recent, SameDescription);
        }

        if (await store.MostUsedCreditCardAsync(clock.Today.AddDays(-MostUsedDays), cancellationToken) is { } mostUsed)
        {
            return new DefaultChoice(mostUsed, MostUsedCard);
        }

        var options = (await catalog.GetAsync(cancellationToken)).CreditCards
            .Select(c => c.Name).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        throw new DomainException(new DomainError(
            CardRequiredCode,
            "Não sei em qual cartão lançar. Pergunte à pessoa e informe o cartão.",
            DomainErrorKind.Validation,
            Extensions: new Dictionary<string, object?> { ["field"] = "creditCard", ["options"] = options }));
    }
}
