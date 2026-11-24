using FinMy.Application.OAuth;

namespace FinMy.Application.Abstractions;

/// <summary>Aplicativos conectados da pessoa (008 FR-003, research R3): as autorizações OAuth do conector, com o último uso.</summary>
public interface IConnectedAppsStore
{
    /// <summary>Autorizações ativas da pessoa, da mais recente para a mais antiga.</summary>
    Task<IReadOnlyList<ConnectedApp>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Revoga a autorização e os tokens dela; devolve <c>false</c> se não existe, não é da pessoa ou já foi revogada.</summary>
    Task<bool> DisconnectAsync(Guid userId, string authorizationId, CancellationToken cancellationToken);

    /// <summary>Anota o uso da autorização, no máximo uma vez a cada 5 minutos.</summary>
    Task RecordUseAsync(string authorizationId, Guid userId, CancellationToken cancellationToken);
}
