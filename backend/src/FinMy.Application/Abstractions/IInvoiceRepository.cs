using FinMy.Domain.Cards;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Abstractions;

/// <summary>Faturas (<c>invoices</c>), criadas sob demanda (003 research R2).</summary>
public interface IInvoiceRepository
{
    /// <summary>
    /// Devolve a fatura do cartão que vence em <paramref name="dueMonth"/>, criando a linha se ainda não existir, com
    /// <c>INSERT … ON CONFLICT DO NOTHING</c> seguido de <c>SELECT</c>: duas chamadas simultâneas acabam na mesma linha.
    /// As datas são gravadas com <see cref="InvoiceCycle"/> no momento da criação. Participa da transação em andamento.
    /// </summary>
    /// <exception cref="InvalidOperationException">O cartão não existe no Espaço do contexto.</exception>
    Task<Invoice> GetOrCreateAsync(Guid creditCardId, YearMonth dueMonth, CancellationToken cancellationToken);

    /// <summary>A fatura rastreada do cartão no mês, ou <c>null</c> se a linha ainda não existe.</summary>
    Task<Invoice?> FindAsync(Guid creditCardId, YearMonth dueMonth, CancellationToken cancellationToken);
}
