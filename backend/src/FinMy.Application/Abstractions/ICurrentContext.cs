using FinMy.Domain.Common;

namespace FinMy.Application.Abstractions;

/// <summary>Contexto ambiente da requisição ou do job: quem age, em qual Espaço e por qual canal.</summary>
public interface ICurrentContext
{
    Guid? UserId { get; }

    Guid? TenantId { get; }

    Channel Channel { get; }

    bool IsAuthenticated { get; }

    bool IsSystem { get; }

    /// <summary>Espaço do contexto, ou <see cref="Guid.Empty"/> quando não há (o filtro de Espaço então não retorna linhas).</summary>
    Guid TenantIdOrEmpty { get; }

    /// <summary>Identificador do lote de exclusão corrente (um por requisição/escopo, até <see cref="BeginDeletionBatch"/>).</summary>
    Guid DeletionBatchId { get; }

    /// <summary>
    /// Abre um novo lote de exclusão (UUIDv7) e o torna o corrente: tudo que for excluído a partir daqui
    /// compartilha o identificador devolvido, que o cliente usa para desfazer (<c>POST /deletions/{batchId}/restore</c>).
    /// </summary>
    Guid BeginDeletionBatch();

    /// <summary>Entra em escopo de sistema para o Espaço informado; descartar o retorno restaura o contexto anterior.</summary>
    IDisposable BeginSystemScope(Guid tenantId);
}
