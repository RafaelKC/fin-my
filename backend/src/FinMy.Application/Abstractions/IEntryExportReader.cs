using FinMy.Application.Exports;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Abstractions;

/// <summary>Leitura dos lançamentos de um intervalo de meses para a exportação CSV (006 research R4), sempre no Espaço do contexto.</summary>
public interface IEntryExportReader
{
    /// <summary>
    /// Linhas do período, em qualquer ordem: Entradas/Saídas pelo Mês de referência, Compras e Pagamentos de fatura pelo mês de
    /// vencimento da fatura e Transferências pelo mês da data.
    /// </summary>
    Task<IReadOnlyList<ExportRow>> ReadAsync(YearMonth firstMonth, YearMonth lastMonth, CancellationToken cancellationToken);
}
