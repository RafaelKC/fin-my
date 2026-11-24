namespace FinMy.Application.Abstractions;

/// <summary>
/// Revoga as autorizações OAuth (conectores como o Claude, 008) de uma pessoa. Trocar ou redefinir a senha
/// chama <see cref="RevokeAllAsync"/>: o aplicativo precisa ser autorizado de novo.
/// </summary>
public interface IOAuthAuthorizationRevoker
{
    /// <summary>Revoga todas as autorizações e tokens da pessoa; vale já na próxima chamada do aplicativo.</summary>
    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken);
}
