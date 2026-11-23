using FinMy.Domain.Monetary;

namespace FinMy.Domain.Cards;

/// <summary>Como o valor informado se relaciona com as parcelas.</summary>
public enum InstallmentMode
{
    /// <summary>Valor total dividido em N (sobra de centavos na 1ª parcela, RT-02).</summary>
    TotalAmount,

    /// <summary>Valor de cada parcela; N parcelas iguais.</summary>
    InstallmentAmount,

    /// <summary>Parcelamento em andamento: gera as parcelas p..N, cada uma com o valor informado.</summary>
    InProgress,
}

/// <summary>Uma parcela gerada: número k, valor e fatura (mês de vencimento).</summary>
public readonly record struct GeneratedInstallment(int Number, long AmountCents, YearMonth DueMonth);

/// <summary>Gera as parcelas de uma compra parcelada (research R1).</summary>
public static class InstallmentGenerator
{
    /// <summary>
    /// <paramref name="n"/> = total de parcelas; <paramref name="firstNumber"/> = próxima parcela (só em andamento,
    /// ignorado nos outros modos); <paramref name="firstInvoiceMonth"/> = fatura da primeira parcela gerada.
    /// </summary>
    public static IReadOnlyList<GeneratedInstallment> Generate(
        InstallmentMode mode, int n, int firstNumber, long amountCents, YearMonth firstInvoiceMonth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountCents);

        if (mode == InstallmentMode.InProgress)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(firstNumber, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(firstNumber, n);
        }
        else
        {
            firstNumber = 1;
        }

        var count = n - firstNumber + 1;
        var amounts = mode == InstallmentMode.TotalAmount
            ? MoneySplitter.Split(amountCents, n)
            : Enumerable.Repeat(amountCents, count).ToArray();

        var result = new GeneratedInstallment[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = new GeneratedInstallment(firstNumber + i, amounts[i], firstInvoiceMonth.AddMonths(i));
        }

        return result;
    }
}
