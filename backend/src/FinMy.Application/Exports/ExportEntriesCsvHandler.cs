using System.Text;
using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Exports;

/// <summary>Arquivo CSV pronto para download (com BOM UTF-8, para o Excel reconhecer os acentos).</summary>
public sealed record CsvExport(string FileName, byte[] Content);

/// <summary><c>GET /exports/entries.csv</c> (006 US4, research R4): valida o intervalo e monta o arquivo.</summary>
public sealed class ExportEntriesCsvHandler(IEntryExportReader reader)
{
    public const int MaxMonths = 120;

    private static readonly YearMonth First = YearMonth.Of(2000, 1);
    private static readonly YearMonth Last = YearMonth.Of(2100, 12);

    public async Task<CsvExport> HandleAsync(string? from, string? to, CancellationToken cancellationToken)
    {
        if (!YearMonth.TryParse(from, out var start) || !YearMonth.TryParse(to, out var end) || start < First || end > Last)
        {
            throw Invalid("export.range_invalid", "Informe o mês inicial e o final, entre 01/2000 e 12/2100, no formato AAAA-MM");
        }

        if (end < start)
        {
            throw Invalid("export.range_invalid", "O mês final não pode ser anterior ao inicial");
        }

        if ((end.Year - start.Year) * 12 + end.Month - start.Month + 1 > MaxMonths)
        {
            throw Invalid("export.range_too_long", $"O período pode ter no máximo {MaxMonths} meses");
        }

        var rows = await reader.ReadAsync(start, end, cancellationToken);
        var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(EntriesCsvWriter.Write(rows))).ToArray();
        return new CsvExport($"fin-my-{start}_{end}.csv", content);
    }

    private static DomainException Invalid(string code, string message) =>
        new(new DomainError(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { ["range"] = [message] }));
}
