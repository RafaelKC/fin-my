using FinMy.Application.Auth;
using FinMy.Application.Profile;

namespace FinMy.Application.Abstractions;

/// <summary>Persistência do perfil (US5): nome da pessoa, senha e nome do Espaço.</summary>
public interface IProfileStore
{
    /// <summary>Usuário e Espaço da pessoa, ou <c>null</c> se a conta não existe mais.</summary>
    Task<MeResult?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Altera o nome do usuário e devolve o usuário e o Espaço, ou <c>null</c> se a conta não existe mais.</summary>
    Task<MeResult?> RenameUserAsync(Guid userId, string name, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Hash da senha atual, ou <c>null</c> se a conta não existe mais.</summary>
    Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Grava o novo hash e renova o <c>security_stamp</c>.</summary>
    Task ChangePasswordAsync(Guid userId, string newPasswordHash, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Renomeia o Espaço e devolve-o com o papel da pessoa, ou <c>null</c> se a pessoa não participa dele.</summary>
    Task<TenantSummary?> RenameTenantAsync(Guid tenantId, Guid userId, string name, CancellationToken cancellationToken);
}
