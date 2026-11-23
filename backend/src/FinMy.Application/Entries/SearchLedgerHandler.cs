using System.Globalization;
using System.Text;
using FinMy.Application.Abstractions;
using FinMy.Application.Tags;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Entries;

/// <summary>Parâmetros de <c>GET /entries/search</c> como chegam da borda (texto).</summary>
public sealed record SearchLedgerCommand(
    string? Text,
    string? Kinds,
    string? WalletIds,
    string? Status,
    DateOnly? From,
    DateOnly? To,
    string? Cursor,
    int? Limit,
    string? TagIds = null);

/// <summary>Resultado da busca; <see cref="NextCursor"/> é opaco e <c>null</c> na última página.</summary>
public sealed record SearchLedgerResult(IReadOnlyList<LedgerItemView> Items, long TotalCents, int TotalCount, string? NextCursor);

/// <summary>Busca em Entradas, Saídas, Transferências e Compras no cartão de qualquer mês (FR-025, research R10).</summary>
public sealed class SearchLedgerHandler(ILedgerSearchStore store)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    public const int TextMaxLength = 80;
    public const int WalletIdsMax = 50;

    public async Task<SearchLedgerResult> HandleAsync(SearchLedgerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var text = command.Text is { Length: > 0 } t ? t : null;
        if (text is { Length: > TextMaxLength })
        {
            throw new DomainException(EntryErrors.Field("q.too_long", "q", $"Use no máximo {TextMaxLength} caracteres na busca"));
        }

        var normalized = text is null ? null : TextNormalizer.Normalize(text);
        var limit = command.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
        {
            throw new DomainException(EntryErrors.Field("limit.invalid", "limit", $"Use um limite entre 1 e {MaxLimit}"));
        }

        if (command.From is { } from && command.To is { } to && from > to)
        {
            throw new DomainException(EntryErrors.Field("period.invalid", "to", "A data final deve ser igual ou posterior à inicial"));
        }

        var criteria = new LedgerSearchCriteria(
            string.IsNullOrEmpty(normalized) ? null : normalized,
            ParseKinds(command.Kinds),
            ParseWalletIds(command.WalletIds),
            string.IsNullOrEmpty(command.Status) ? null : EntryInput.ParseStatus(command.Status),
            command.From,
            command.To,
            string.IsNullOrEmpty(command.Cursor) ? null : DecodeCursor(command.Cursor),
            limit,
            TagSelector.ParseFilter(command.TagIds));

        var page = await store.SearchAsync(criteria, cancellationToken);
        return new SearchLedgerResult(page.Items, page.TotalCents, page.TotalCount, page.Next is { } next ? EncodeCursor(next) : null);
    }

    private static List<LedgerItemKind> ParseKinds(string? text)
    {
        var kinds = new List<LedgerItemKind>();
        foreach (var part in Split(text))
        {
            var kind = part switch
            {
                "income" => LedgerItemKind.Income,
                "expense" => LedgerItemKind.Expense,
                "transfer" => LedgerItemKind.Transfer,
                "card_purchase" => LedgerItemKind.CardPurchase,
                _ => throw new DomainException(EntryErrors.Field("kinds.invalid", "kinds", "Use income, expense, transfer ou card_purchase")),
            };
            if (!kinds.Contains(kind))
            {
                kinds.Add(kind);
            }
        }

        return kinds;
    }

    private static List<Guid> ParseWalletIds(string? text)
    {
        var ids = new List<Guid>();
        foreach (var part in Split(text))
        {
            if (!Guid.TryParse(part, out var id))
            {
                throw new DomainException(EntryErrors.Field("walletIds.invalid", "walletIds", "Informe ids de carteira válidos"));
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids.Count <= WalletIdsMax
            ? ids
            : throw new DomainException(EntryErrors.Field("walletIds.invalid", "walletIds", $"Informe no máximo {WalletIdsMax} carteiras"));
    }

    private static string[] Split(string? text) =>
        (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Cursor opaco: base64url de <c>data|criadoEm|id</c>.</summary>
    internal static string EncodeCursor(LedgerCursor cursor)
    {
        var raw = string.Create(CultureInfo.InvariantCulture, $"{cursor.Date:yyyy-MM-dd}|{cursor.CreatedAtUtcTicks}|{cursor.Id:D}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static LedgerCursor DecodeCursor(string text)
    {
        var invalid = new DomainException(EntryErrors.Field("cursor.invalid", "cursor", "Cursor inválido"));
        try
        {
            var padded = text.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('|');
            if (parts.Length == 3
                && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks <= DateTime.MaxValue.Ticks
                && Guid.TryParseExact(parts[2], "D", out var id))
            {
                return new LedgerCursor(date, ticks, id);
            }
        }
        catch (FormatException)
        {
            // cai no erro padrão abaixo
        }

        throw invalid;
    }
}
