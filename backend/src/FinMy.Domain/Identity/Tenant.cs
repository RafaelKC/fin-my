using FinMy.Domain.Common;

namespace FinMy.Domain.Identity;

/// <summary>
/// Espaço (<c>tenants</c>). Tabela global — é <see cref="IAudited"/>, mas não <see cref="ITenantEntity"/>
/// nem <see cref="ISoftDeletable"/>. A auditoria é preenchida pelo <c>AuditInterceptor</c>.
/// </summary>
public sealed class Tenant : IEntity, IAudited
{
    // Construtor para o EF Core.
    private Tenant()
    {
    }

    public Tenant(string name)
    {
        Id = Guid.CreateVersion7();
        Name = name.Trim();
    }

    public Guid Id { get; private set; }

    /// <summary>Altera o nome do Espaço; a auditoria (<c>updated_*</c>) é preenchida pelo interceptor.</summary>
    public void Rename(string name) => Name = name.Trim();

    public string Name { get; private set; } = string.Empty;

    /// <summary>Estado do onboarding por etapa (usado pela feature 006); nasce vazio.</summary>
    public Dictionary<string, string> OnboardingSteps { get; private set; } = [];

    public DateTimeOffset? OnboardingDismissedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; internal set; }

    public Guid CreatedBy { get; internal set; }

    public Channel CreatedVia { get; internal set; }

    public DateTimeOffset UpdatedAt { get; internal set; }

    public Guid UpdatedBy { get; internal set; }

    public Channel UpdatedVia { get; internal set; }
}
