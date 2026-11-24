using System.Globalization;
using System.Text;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Exports;

/// <summary>Monta o CSV de lançamentos (006 research R4): separador <c>;</c>, vírgula decimal, datas DD/MM/AAAA, RFC 4180.</summary>
public static class EntriesCsvWriter
{
    public const string Header = "Tipo;Descrição;Data;Mês de referência;Valor;Situação;Carteira;Cartão;Fatura;Parcela;Tags";

    public static string Write(IEnumerable<ExportRow> rows)
    {
        var text = new StringBuilder(Header).Append("\r\n");
        foreach (var row in rows.OrderBy(r => r.PeriodMonth).ThenBy(r => r.Date).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id))
        {
            text.AppendJoin(';', Fields(row).Select(Escape)).Append("\r\n");
        }

        return text.ToString();
    }

    private static string[] Fields(ExportRow row) =>
    [
        TypeName(row.Type),
        row.Description,
        row.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        Month(row.ReferenceMonth),
        Amount(row.AmountCents),
        Situation(row),
        row.Wallet,
        row.Card ?? string.Empty,
        Month(row.Invoice),
        row.Installment ?? string.Empty,
        string.Join(", ", row.Tags),
    ];

    private static string TypeName(ExportRowType type) => type switch
    {
        ExportRowType.Income => "Entrada",
        ExportRowType.Expense => "Saída",
        ExportRowType.Purchase => "Compra",
        ExportRowType.InvoicePayment => "Pagamento de fatura",
        _ => "Transferência",
    };

    private static string Situation(ExportRow row) => row.Type switch
    {
        ExportRowType.Income or ExportRowType.Expense => (row.Type, row.Status) switch
        {
            (_, EntryStatus.Planned) => "Prevista",
            (ExportRowType.Income, _) => "Recebida",
            _ => "Paga",
        },
        ExportRowType.InvoicePayment => "Paga",
        ExportRowType.Transfer => "Efetivada",
        _ => string.Empty,
    };

    private static string Month(YearMonth? month) =>
        month is { } m ? $"{m.Month:00}/{m.Year:0000}" : string.Empty;

    /// <summary>Reais com vírgula decimal e sem separador de milhar (<c>-1234,56</c>).</summary>
    private static string Amount(long cents)
    {
        var abs = Math.Abs(cents);
        return $"{(cents < 0 ? "-" : string.Empty)}{abs / 100}{','}{abs % 100:00}";
    }

    private static string Escape(string field) =>
        field.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : field;
}
