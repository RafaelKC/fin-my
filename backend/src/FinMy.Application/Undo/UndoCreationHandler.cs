using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Undo;

/// <summary>
/// Desfaz o que o Claude acabou de criar (008 US2.5, research R4): exclui o item e o grupo criado junto (parcelas, divisão)
/// se foi criado por este Usuário pelo canal Claude há no máximo <see cref="Window"/>. Sem confirmação.
/// </summary>
public sealed class UndoCreationHandler(IUndoStore store, ICurrentContext context, IClock clock)
{
    public const string NotAllowedCode = "undo_not_allowed";

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public static readonly DomainError NotFound = new("not_found", "Não encontrei esse lançamento.", DomainErrorKind.NotFound);

    public static readonly DomainError NotAllowed = new(
        NotAllowedCode,
        "Só dá para desfazer o que o Claude criou para você nos últimos 10 minutos. Para excluir este lançamento, use a exclusão com confirmação.",
        DomainErrorKind.Validation);

    /// <returns>Quantos lançamentos, compras ou transferências foram excluídos.</returns>
    public async Task<int> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await store.FindAsync(id, cancellationToken) ?? throw new DomainException(NotFound);

        var age = clock.UtcNow - item.CreatedAt;
        if (item.CreatedVia != Channel.Claude || context.UserId is not { } userId || item.CreatedBy != userId || age > Window)
        {
            throw new DomainException(NotAllowed);
        }

        context.BeginDeletionBatch();
        return await store.DeleteWithGroupAsync(item, cancellationToken);
    }
}
