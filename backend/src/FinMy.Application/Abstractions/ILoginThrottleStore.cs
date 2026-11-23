using FinMy.Domain.Auth;

namespace FinMy.Application.Abstractions;

/// <summary>Contador de tentativas de login por e-mail normalizado, exista ou não a conta (research R5).</summary>
public interface ILoginThrottleStore
{
    /// <summary>Estado atual do e-mail, ou <c>null</c> se não há registro.</summary>
    Task<LoginThrottleState?> GetAsync(string emailNormalized, CancellationToken cancellationToken);

    /// <summary>
    /// Registra uma falha aplicando <see cref="LoginThrottleRule"/> de forma atômica (a linha fica travada durante a
    /// atualização), para tentativas simultâneas não perderem contagem. Devolve o estado resultante.
    /// </summary>
    Task<LoginThrottleState> RegisterFailureAsync(string emailNormalized, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Login com sucesso: zera o registro.</summary>
    Task ResetAsync(string emailNormalized, CancellationToken cancellationToken);
}
