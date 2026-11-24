using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.OAuth;

/// <summary>Lista os aplicativos conectados do usuário do contexto (008 FR-003).</summary>
public sealed class ListConnectedAppsHandler(IConnectedAppsStore apps, ICurrentContext context)
{
    public async Task<IReadOnlyList<ConnectedApp>> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = context.UserId ?? throw ConnectedAppErrors.Unauthorized();
        return await apps.ListAsync(userId, cancellationToken);
    }
}

/// <summary>Desconecta um aplicativo: revoga a autorização e os tokens, e o acesso acaba na próxima chamada (008 SC-004).</summary>
public sealed class DisconnectConnectedAppHandler(IConnectedAppsStore apps, ICurrentContext context)
{
    public async Task HandleAsync(string? id, CancellationToken cancellationToken)
    {
        var userId = context.UserId ?? throw ConnectedAppErrors.Unauthorized();
        if (string.IsNullOrWhiteSpace(id) || !await apps.DisconnectAsync(userId, id, cancellationToken))
        {
            throw new DomainException(new DomainError("connected_app_not_found", "Aplicativo conectado não encontrado.", DomainErrorKind.NotFound));
        }
    }
}

internal static class ConnectedAppErrors
{
    public static DomainException Unauthorized() => new(new DomainError("unauthorized", "Entre novamente para continuar.", DomainErrorKind.Unauthorized));
}
