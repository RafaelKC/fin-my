using FinMy.Domain.Monetary;

namespace FinMy.Domain.Settlements;

/// <summary>
/// Valores de uma Tag de acerto num mês (004 FR-006/FR-007), já somados pela aplicação. O mês das compras é o do
/// vencimento da fatura; o das Entradas e Saídas é o Mês de referência. Valores em centavos, todos positivos
/// (o crédito entra em <see cref="CardCreditCents"/> e é subtraído aqui).
/// </summary>
/// <param name="ExpenseCents">Σ Saídas com a tag (qualquer situação).</param>
/// <param name="CardDebitCents">Σ Compras de Débito com a tag em faturas com vencimento no mês.</param>
/// <param name="CardCreditCents">Σ Compras de Crédito (estornos) com a tag em faturas com vencimento no mês.</param>
/// <param name="ChargedCents">Σ Entradas com a tag (qualquer situação).</param>
/// <param name="ReceivedCents">Σ Entradas Recebidas com a tag.</param>
public readonly record struct SettlementMonthInput(
    long ExpenseCents,
    long CardDebitCents,
    long CardCreditCents,
    long ChargedCents,
    long ReceivedCents);

/// <summary>Números de uma Tag de acerto em um mês (004 FR-006..FR-008).</summary>
/// <param name="SpentCents">Gasto = Saídas + Débitos − Créditos.</param>
/// <param name="ChargedCents">Cobrado = Σ Entradas.</param>
/// <param name="ReceivedCents">Recebido = Σ Entradas Recebidas.</param>
/// <param name="ToChargeCents">A cobrar = Gasto − Cobrado.</param>
/// <param name="ToReceiveCents">A receber = Gasto − Recebido.</param>
/// <param name="AccumulatedToReceiveCents">Saldo acumulado = Σ A receber de todos os meses ≤ o mês; negativo é crédito da pessoa.</param>
public readonly record struct SettlementResult(
    long SpentCents,
    long ChargedCents,
    long ReceivedCents,
    long ToChargeCents,
    long ToReceiveCents,
    long AccumulatedToReceiveCents);

/// <summary>Cálculo puro dos acertos por Tag de acerto (004 FR-006..FR-008, research R3).</summary>
public static class SettlementCalculator
{
    /// <summary>Gasto, Cobrado, Recebido, A cobrar e A receber de um único mês.</summary>
    public static SettlementResult ForMonth(SettlementMonthInput input)
    {
        var spent = checked(input.ExpenseCents + input.CardDebitCents - input.CardCreditCents);
        var toReceive = checked(spent - input.ReceivedCents);
        return new SettlementResult(
            spent,
            input.ChargedCents,
            input.ReceivedCents,
            checked(spent - input.ChargedCents),
            toReceive,
            toReceive);
    }

    /// <summary>
    /// Números de <paramref name="month"/> com o Saldo acumulado somando o A receber de todos os meses
    /// ≤ <paramref name="month"/> presentes em <paramref name="monthlyInputs"/> (meses sem movimento valem zero).
    /// </summary>
    public static SettlementResult Calculate(YearMonth month, IReadOnlyDictionary<YearMonth, SettlementMonthInput> monthlyInputs)
    {
        ArgumentNullException.ThrowIfNull(monthlyInputs);

        var accumulated = 0L;
        foreach (var (m, input) in monthlyInputs)
        {
            if (m <= month)
            {
                accumulated = checked(accumulated + ForMonth(input).ToReceiveCents);
            }
        }

        var current = monthlyInputs.TryGetValue(month, out var found) ? ForMonth(found) : default;
        return current with { AccumulatedToReceiveCents = accumulated };
    }
}
