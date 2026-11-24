using System.Globalization;
using FinMy.Application.Entries;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Api.Mcp;

/// <summary>Leitura dos parâmetros de texto das ferramentas (datas <c>AAAA-MM-DD</c>, meses <c>AAAA-MM</c>).</summary>
internal static class McpArgs
{
    public static DateOnly? Date(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new DomainException(EntryErrors.Field("date.invalid", field, "Use a data no formato AAAA-MM-DD"));
    }

    /// <summary>Mês pedido (a validação do formato é do handler); ausente = o mês de <paramref name="today"/>.</summary>
    public static string Month(string? month, DateOnly today) => string.IsNullOrWhiteSpace(month) ? YearMonth.From(today).ToString() : month.Trim();

    public static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
