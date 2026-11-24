using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Planning;

/// <summary>Valor de um mês do ano anterior numa linha candidata à cópia.</summary>
public sealed record CopyCandidateCell(YearMonth Month, long PlannedCents, int ItemCount);

/// <summary>Linha do ano anterior sem ocorrências de recorrência; partes de divisão ficam em linhas separadas por tag de acerto (US3.3).</summary>
public sealed record CopyCandidateRow(
    string Key, EntryKind Kind, string Description, Guid? SettlementTagId, string? SettlementTagName, IReadOnlyList<CopyCandidateCell> Cells);

public sealed record CopyEstimateMonth(YearMonth Month, long EstimateCents);

public sealed record CopyEstimateCard(Guid CreditCardId, string CreditCardName, IReadOnlyList<CopyEstimateMonth> Months);

public sealed record CopyCandidatesResult(IReadOnlyList<CopyCandidateRow> Rows, IReadOnlyList<CopyEstimateCard> Estimates);

/// <param name="RowKeys">Chaves das linhas escolhidas em <c>copy-candidates</c>.</param>
/// <param name="CopyEstimates">Copiar também as estimativas de fatura.</param>
public sealed record CopyPlanningCommand(string? Year, IReadOnlyList<string>? RowKeys, bool? CopyEstimates);

/// <summary>Quantos lançamentos e estimativas foram criados e quantos foram pulados por já haver valor no destino.</summary>
public sealed record CopyPlanningResult(int Created, int Skipped);
