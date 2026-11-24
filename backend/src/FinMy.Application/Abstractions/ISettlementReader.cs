using FinMy.Domain.Monetary;
using FinMy.Domain.Settlements;

namespace FinMy.Application.Abstractions;

/// <summary>Uma Tag de acerto com os valores mensais (até o mês pedido) que alimentam o <c>SettlementCalculator</c>.</summary>
public sealed record SettlementTagData(
    Guid TagId, string Name, string Color, bool Archived, IReadOnlyDictionary<YearMonth, SettlementMonthInput> Months);

/// <summary>Grupo de um lançamento nos acertos: compõe o Gasto ou o Cobrado.</summary>
public enum SettlementGroup
{
    Spent,
    Charged,
}

/// <summary>Lançamento que compõe um número dos acertos (contracts/openapi.yaml <c>/settlements/{month}/tags/{tagId}/items</c>).</summary>
/// <param name="Type"><c>income</c>, <c>expense</c> ou <c>card_purchase</c>.</param>
/// <param name="SignedCents">Crédito de compra é negativo.</param>
/// <param name="Received">Só nas Entradas: se já foi recebida.</param>
public sealed record SettlementItem(
    SettlementGroup Group, string Type, Guid Id, string Description, DateOnly Date, long SignedCents, bool? Received, Guid? CreditCardId);

/// <summary>Consultas dos acertos por Tag de acerto (004 research R3), sempre no Espaço do contexto.</summary>
public interface ISettlementReader
{
    /// <summary>
    /// Toda Tag de acerto do Espaço (ativa ou arquivada) com os valores de cada mês ≤ <paramref name="month"/>:
    /// Saídas e Entradas pelo Mês de referência, compras pelo vencimento da fatura.
    /// </summary>
    Task<IReadOnlyList<SettlementTagData>> ReadAsync(YearMonth month, CancellationToken cancellationToken);

    /// <summary>Lançamentos de <paramref name="month"/> que compõem os números da tag, em ordem de grupo, data e criação.</summary>
    Task<IReadOnlyList<SettlementItem>> ReadItemsAsync(Guid tagId, YearMonth month, CancellationToken cancellationToken);
}
