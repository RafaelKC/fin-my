using FinMy.Domain.Identity;

namespace FinMy.Application.Abstractions;

/// <summary>Estado do guia de primeiros passos do Espaço do contexto (006 research R3).</summary>
public interface IOnboardingStore
{
    /// <summary>O Espaço do contexto, rastreado para gravação.</summary>
    Task<Tenant?> FindTenantAsync(CancellationToken cancellationToken);

    /// <summary>O Espaço tem alguma carteira (ativa ou arquivada).</summary>
    Task<bool> HasWalletsAsync(CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
