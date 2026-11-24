using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Catalog;

public sealed record CatalogWallet(Guid Id, string Name, bool IsPrimary);

public sealed record CatalogCreditCard(Guid Id, string Name, int ClosingDay, int DueDay);

public sealed record CatalogTag(Guid Id, string Name, bool IsSettlement);

/// <summary>Carteiras, cartões e tags <b>ativos</b> do Espaço (008 contracts <c>list_catalog</c>).</summary>
public sealed record CatalogView(
    DateOnly Today,
    IReadOnlyList<CatalogWallet> Wallets,
    IReadOnlyList<CatalogCreditCard> CreditCards,
    IReadOnlyList<CatalogTag> Tags);

/// <summary>Nomes de carteiras, cartões e tags (qualquer estado) para dar nome aos ids das respostas.</summary>
public sealed class CatalogNames(
    IReadOnlyDictionary<Guid, string> wallets, IReadOnlyDictionary<Guid, string> creditCards, IReadOnlyDictionary<Guid, string> tags)
{
    public string Wallet(Guid id) => wallets.GetValueOrDefault(id, string.Empty);

    public string CreditCard(Guid id) => creditCards.GetValueOrDefault(id, string.Empty);

    public string Tag(Guid id) => tags.GetValueOrDefault(id, string.Empty);
}

/// <summary>
/// Resolve nomes (como a pessoa os diz ao Claude) em ids (008 research R4, FR-006): só itens ativos, comparação exata pelo
/// <see cref="TextNormalizer"/> (sem busca por trecho). Zero ou mais de um resultado vira <c>ambiguous_or_not_found</c> com as
/// opções ativas daquele tipo, para o Claude perguntar em vez de escolher.
/// </summary>
public sealed class CatalogResolver(IWalletStore wallets, ICreditCardStore cards, ITagStore tags, IClock clock)
{
    public const string ErrorCode = "ambiguous_or_not_found";

    public async Task<CatalogView> GetAsync(CancellationToken cancellationToken)
    {
        var activeWallets = await wallets.ListAsync(WalletArchivedFilter.ActiveOnly, cancellationToken);
        var activeCards = await cards.ListOverviewsAsync(CreditCardArchivedFilter.ActiveOnly, clock.Today, cancellationToken);
        var activeTags = await tags.ListAsync(TagArchivedFilter.ActiveOnly, clock.Today, cancellationToken);

        return new CatalogView(
            clock.Today,
            activeWallets.Select(w => new CatalogWallet(w.Id, w.Name, w.IsPrimary)).ToList(),
            activeCards.Select(c => new CatalogCreditCard(c.Card.Id, c.Card.Name, c.Card.ClosingDay, c.Card.DueDay)).ToList(),
            activeTags.Select(t => new CatalogTag(t.Id, t.Name, t.IsSettlement)).ToList());
    }

    public async Task<CatalogNames> LoadNamesAsync(CancellationToken cancellationToken)
    {
        var allWallets = await wallets.ListAsync(WalletArchivedFilter.All, cancellationToken);
        var allCards = await cards.ListOverviewsAsync(CreditCardArchivedFilter.All, clock.Today, cancellationToken);
        var allTags = await tags.ListAsync(TagArchivedFilter.All, clock.Today, cancellationToken);
        return new CatalogNames(
            allWallets.ToDictionary(w => w.Id, w => w.Name),
            allCards.ToDictionary(c => c.Card.Id, c => c.Card.Name),
            allTags.ToDictionary(t => t.Id, t => t.Name));
    }

    public async Task<Guid> ResolveWalletAsync(string? name, string field, CancellationToken cancellationToken)
    {
        var items = (await wallets.ListAsync(WalletArchivedFilter.ActiveOnly, cancellationToken)).Select(w => (w.Id, w.Name));
        return Pick(items, name, field, Noun.Wallet);
    }

    public async Task<IReadOnlyList<Guid>> ResolveWalletsAsync(IEnumerable<string>? names, string field, CancellationToken cancellationToken)
    {
        if (names is null)
        {
            return [];
        }

        var items = (await wallets.ListAsync(WalletArchivedFilter.ActiveOnly, cancellationToken)).Select(w => (w.Id, w.Name)).ToList();
        return names.Select(n => Pick(items, n, field, Noun.Wallet)).Distinct().ToList();
    }

    public async Task<Guid> ResolveCreditCardAsync(string? name, string field, CancellationToken cancellationToken)
    {
        var items = (await cards.ListOverviewsAsync(CreditCardArchivedFilter.ActiveOnly, clock.Today, cancellationToken)).Select(c => (c.Card.Id, c.Card.Name));
        return Pick(items, name, field, Noun.CreditCard);
    }

    /// <param name="settlementOnly">Só Tags de acerto (ex.: <c>get_settlements</c>); com <c>false</c>, qualquer tag ativa.</param>
    public async Task<Guid> ResolveTagAsync(string? name, string field, bool settlementOnly, CancellationToken cancellationToken)
    {
        var items = await ActiveTagsAsync(settlementOnly, cancellationToken);
        return Pick(items, name, field, settlementOnly ? Noun.SettlementTag : Noun.Tag);
    }

    public async Task<IReadOnlyList<Guid>> ResolveTagsAsync(IEnumerable<string>? names, string field, CancellationToken cancellationToken)
    {
        if (names is null)
        {
            return [];
        }

        var items = (await ActiveTagsAsync(false, cancellationToken)).ToList();
        return names.Select(n => Pick(items, n, field, Noun.Tag)).Distinct().ToList();
    }

    private async Task<IEnumerable<(Guid Id, string Name)>> ActiveTagsAsync(bool settlementOnly, CancellationToken cancellationToken)
        => (await tags.ListAsync(TagArchivedFilter.ActiveOnly, clock.Today, cancellationToken))
            .Where(t => !settlementOnly || t.IsSettlement)
            .Select(t => (t.Id, t.Name));

    private static Guid Pick(IEnumerable<(Guid Id, string Name)> items, string? name, string field, Noun noun)
    {
        var all = items.ToList();
        var wanted = TextNormalizer.Normalize(name ?? string.Empty);
        var matches = wanted.Length == 0 ? [] : all.Where(i => TextNormalizer.Normalize(i.Name) == wanted).ToList();
        if (matches.Count == 1)
        {
            return matches[0].Id;
        }

        var options = all.Select(i => i.Name).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        var message = matches.Count == 0
            ? $"Não encontrei {noun.The} \"{name}\". Escolha uma das opções."
            : $"Há mais de {noun.One} com o nome \"{name}\". Escolha uma das opções.";
        throw new DomainException(new DomainError(
            ErrorCode,
            message,
            DomainErrorKind.Validation,
            Extensions: new Dictionary<string, object?> { ["field"] = field, ["options"] = options }));
    }

    private sealed record Noun(string The, string One)
    {
        public static readonly Noun Wallet = new("a carteira", "uma carteira");
        public static readonly Noun CreditCard = new("o cartão", "um cartão");
        public static readonly Noun Tag = new("a tag", "uma tag");
        public static readonly Noun SettlementTag = new("a tag de acerto", "uma tag de acerto");
    }
}
