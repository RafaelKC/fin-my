using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Splits;

/// <summary>Como o total é repartido (004 FR-012).</summary>
public enum SplitMode
{
    /// <summary>Partes iguais; a sobra de centavos vai para a primeira (RT-02).</summary>
    Equal,

    /// <summary>Valores informados; a soma tem de fechar com o total.</summary>
    Amounts,
}

/// <summary>Parte pedida: <see cref="SettlementTagId"/> nulo é "Eu" (sem Tag de acerto); <see cref="AmountCents"/> só vale em <see cref="SplitMode.Amounts"/>.</summary>
public readonly record struct SplitPartRequest(Guid? SettlementTagId, long? AmountCents = null);

/// <summary>Parte calculada, na ordem pedida.</summary>
public readonly record struct SplitPart(Guid? SettlementTagId, long AmountCents);

/// <summary>Planejamento puro da divisão de um lançamento entre pessoas (004 FR-012..FR-014, research R5).</summary>
public static class SplitPlanner
{
    public const int MinParts = 2;
    public const int MaxParts = 10;

    public static IReadOnlyList<SplitPart> Plan(long totalCents, SplitMode mode, IReadOnlyList<SplitPartRequest> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        if (totalCents <= 0 || totalCents > Money.MaxCents)
        {
            throw Rules.AmountError(totalCents <= 0 ? ValueExpressionErrors.MustBePositive : ValueExpressionErrors.TooLarge, "amount");
        }

        if (parts.Count is < MinParts or > MaxParts)
        {
            throw Rules.Invalid("split.parts_count", "split.parts", $"Divida em {MinParts} a {MaxParts} partes");
        }

        var tags = parts.Where(p => p.SettlementTagId is not null).Select(p => p.SettlementTagId).ToList();
        if (tags.Distinct().Count() != tags.Count || parts.Count(p => p.SettlementTagId is null) > 1)
        {
            throw Rules.Invalid("split.duplicate_part", "split.parts", "Cada pessoa só pode aparecer uma vez na divisão");
        }

        return mode == SplitMode.Equal ? Equal(totalCents, parts) : Amounts(totalCents, parts);
    }

    private static List<SplitPart> Equal(long totalCents, IReadOnlyList<SplitPartRequest> parts)
    {
        var shares = MoneySplitter.Split(totalCents, parts.Count);
        return parts.Select((p, i) => new SplitPart(p.SettlementTagId, shares[i])).ToList();
    }

    private static List<SplitPart> Amounts(long totalCents, IReadOnlyList<SplitPartRequest> parts)
    {
        var result = new List<SplitPart>(parts.Count);
        var sum = 0L;
        foreach (var part in parts)
        {
            if (part.AmountCents is not { } cents || cents <= 0)
            {
                throw Rules.AmountError(ValueExpressionErrors.MustBePositive, "split.parts");
            }

            sum = checked(sum + cents);
            result.Add(new SplitPart(part.SettlementTagId, cents));
        }

        if (sum != totalCents)
        {
            var difference = totalCents - sum;
            throw new DomainException(new DomainError(
                "split.sum_mismatch",
                $"A soma das partes difere do total em {Math.Abs(difference) / 100m:N2}",
                DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["split.parts"] = ["A soma das partes deve ser igual ao total"] },
                Extensions: new Dictionary<string, object?> { ["differenceCents"] = difference }));
        }

        return result;
    }
}
