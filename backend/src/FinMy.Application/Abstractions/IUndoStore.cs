using FinMy.Domain.Common;

namespace FinMy.Application.Abstractions;

/// <summary>Tipo de item que o <c>undo_creation</c> alcança.</summary>
public enum CreatedItemType
{
    Entry,
    CardPurchase,
    Transfer,
}

/// <summary>Quem, como e quando criou o item (auditoria), para decidir se o Claude pode desfazê-lo.</summary>
public sealed record CreatedItem(CreatedItemType Type, Guid Id, Guid CreatedBy, Channel CreatedVia, DateTimeOffset CreatedAt);

/// <summary>Acesso ao que foi criado, para desfazer (008 research R4).</summary>
public interface IUndoStore
{
    /// <summary>Lançamento, compra ou transferência do Espaço com o id; <c>null</c> se não existe (ou já foi excluído).</summary>
    Task<CreatedItem?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Exclui (lógico, no lote corrente do contexto) o item e o grupo criado junto: as partes da divisão e as parcelas do
    /// parcelamento. Devolve quantos lançamentos/compras/transferências foram excluídos.
    /// </summary>
    Task<int> DeleteWithGroupAsync(CreatedItem item, CancellationToken cancellationToken);
}
