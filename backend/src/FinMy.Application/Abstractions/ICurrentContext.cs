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

    /// <summary>Identificador do lote de exclusão corrente (um por requisição/escopo).</summary>
    Guid DeletionBatchId { get; }

    /// <summary>Entra em escopo de sistema para o Espaço informado; descartar o retorno restaura o contexto anterior.</summary>
    IDisposable BeginSystemScope(Guid tenantId);
}
