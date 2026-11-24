using FinMy.Application.Abstractions;
using FinMy.Application.Tags;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Settlements;

namespace FinMy.Application.Settlements;

/// <summary>Linha de <c>GET /settlements/{month}</c>: números de uma Tag de acerto no mês (FR-006..FR-009).</summary>
public sealed record SettlementRow(Guid TagId, string TagName, string TagColor, bool Archived, SettlementResult Figures);

internal static class SettlementMonth
{
    private static readonly YearMonth First = YearMonth.Of(2000, 1);
    private static readonly YearMonth Last = YearMonth.Of(2100, 12);

    public static YearMonth Parse(string? month)
    {
        if (!YearMonth.TryParse(month, out var parsed) || parsed < First || parsed > Last)
        {
            const string message = "Use um mês entre 01/2000 e 12/2100, no formato AAAA-MM";
            throw new DomainException(new DomainError(
                "month.invalid", message, DomainErrorKind.Validation, new Dictionary<string, string[]> { ["month"] = [message] }));
        }

        return parsed;
    }
}

/// <summary>
/// <c>GET /settlements/{month}</c>: uma linha por Tag de acerto ativa mais as arquivadas com algum valor ≠ 0 no mês
/// (FR-009, research R3), em ordem alfabética.
/// </summary>
public sealed class GetSettlementsHandler(ISettlementReader reader)
{
    public async Task<IReadOnlyList<SettlementRow>> HandleAsync(string? month, CancellationToken cancellationToken)
    {
        var parsed = SettlementMonth.Parse(month);
        var tags = await reader.ReadAsync(parsed, cancellationToken);

        return tags
            .Select(t => new SettlementRow(t.TagId, t.Name, t.Color, t.Archived, SettlementCalculator.Calculate(parsed, t.Months)))
            .Where(r => !r.Archived || r.Figures.SpentCents != 0 || r.Figures.ChargedCents != 0 || r.Figures.ReceivedCents != 0)
            .OrderBy(r => r.TagName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.TagId)
            .ToList();
    }
}

/// <summary><c>GET /settlements/{month}/tags/{tagId}/items</c>: os lançamentos que compõem Gasto e Cobrado da tag no mês.</summary>
public sealed class GetSettlementItemsHandler(ISettlementReader reader, ITagStore tags)
{
    public async Task<IReadOnlyList<SettlementItem>> HandleAsync(string? month, Guid tagId, CancellationToken cancellationToken)
    {
        // A tag vem antes do mês: id de outro Espaço na rota é sempre 404, mesmo com o mês inválido.
        _ = await tags.FindAsync(tagId, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
        var parsed = SettlementMonth.Parse(month);
        return await reader.ReadItemsAsync(tagId, parsed, cancellationToken);
    }
}
