using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Months;

namespace FinMy.Application.Months;

/// <summary><c>GET /months/{month}</c> (US3, FR-014..FR-019, research R4).</summary>
public sealed class GetMonthHandler(IMonthReader reader, IClock clock)
{
    private static readonly YearMonth First = YearMonth.Of(2000, 1);
    private static readonly YearMonth Last = YearMonth.Of(2100, 12);

    public async Task<MonthViewResult> HandleAsync(string? month, string? walletIds, CancellationToken cancellationToken)
    {
        if (!YearMonth.TryParse(month, out var parsed) || parsed < First || parsed > Last)
        {
            throw Invalid("month.invalid", "month", "Use um mês entre 01/2000 e 12/2100, no formato AAAA-MM");
        }

        var filter = ParseWalletIds(walletIds);
        var today = clock.Today;
        var data = await reader.ReadAsync(parsed, filter, today, cancellationToken);

        // Extensão da 003 (RT-07, research R8): faturas entram nas Saídas, no mês de vencimento (Previsto: valor da fatura; Realizado: pagamentos).
        var summary = MonthSummaryCalculator.Calculate(parsed, data.Wallets, data.EntryTotals, data.TransferTotals, data.InvoiceFigures);

        IReadOnlyList<Guid>? applied = filter is null ? null : data.Wallets.Select(w => w.WalletId).ToList();
        return new MonthViewResult(parsed.ToString(), today, applied, summary, data.Entries, data.Transfers, data.Invoices, data.Pending);
    }

    private static List<Guid>? ParseWalletIds(string? walletIds)
    {
        if (string.IsNullOrWhiteSpace(walletIds))
        {
            return null;
        }

        var ids = new List<Guid>();
        foreach (var part in walletIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Guid.TryParse(part, out var id))
            {
                throw Invalid("wallet_ids.invalid", "walletIds", "Informe ids de carteira válidos, separados por vírgula");
            }

            ids.Add(id);
        }

        return ids.Count == 0 ? null : ids.Distinct().ToList();
    }

    private static DomainException Invalid(string code, string field, string message) =>
        new(new DomainError(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] }));
}
