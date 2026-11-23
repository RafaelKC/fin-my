namespace FinMy.Application.Abstractions;

/// <summary>
/// Diz se uma sessão (<c>sid</c> = <c>family_id</c> do refresh token) segue ativa, com cache em memória
/// invalidado na hora da revogação (001 research R3). Vale para uma única instância da API.
/// </summary>
public interface ISessionRegistry
{
    /// <summary>A sessão tem ao menos um refresh token não revogado e dentro da validade?</summary>
    Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Esquece o que se sabe da sessão; chamado após revogá-la para valer já na próxima requisição.</summary>
    void Forget(Guid sessionId);
}
