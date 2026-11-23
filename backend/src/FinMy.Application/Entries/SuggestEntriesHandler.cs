using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Entries;

/// <summary>Sugestões de descrição (FR-011, research R8): até 6, por usos, nos últimos 12 meses, sem diferenciar acentos nem caixa.</summary>
public sealed class SuggestEntriesHandler(IEntryStore store, IClock clock)
{
    public const int MaxResults = 6;
    public const int MaxQueryLength = 80;
    public const string CardPurchaseKind = "card_purchase";

    public async Task<IReadOnlyList<EntrySuggestion>> HandleAsync(string? q, string? kind, CancellationToken cancellationToken)
    {
        EntryKind? parsedKind = kind == CardPurchaseKind ? null : EntryInput.ParseKind(kind);
        var text = q?.Trim() ?? string.Empty;
        if (text.Length > MaxQueryLength)
        {
            throw new DomainException(EntryErrors.Field("description.too_long", "q", $"Use até {MaxQueryLength} caracteres"));
        }

        if (text.Length == 0)
        {
            return [];
        }

        return await store.SuggestAsync(TextNormalizer.Normalize(text), parsedKind, clock.Today.AddMonths(-12), MaxResults, cancellationToken);
    }
}
