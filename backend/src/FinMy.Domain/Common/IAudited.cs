namespace FinMy.Domain.Common;

/// <summary>Campos de auditoria preenchidos automaticamente pelos interceptors.</summary>
public interface IAudited
{
    DateTimeOffset CreatedAt { get; }

    Guid CreatedBy { get; }

    Channel CreatedVia { get; }

    DateTimeOffset UpdatedAt { get; }

    Guid UpdatedBy { get; }

    Channel UpdatedVia { get; }
}
