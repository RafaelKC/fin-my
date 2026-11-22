namespace FinMy.Domain.Common;

/// <summary>
/// Base padrão das entidades de um Espaço. <see cref="TenantId"/> e a auditoria só são
/// preenchidos pelos interceptors (setters <c>internal</c>); o código de aplicação não os atribui.
/// </summary>
public abstract class TenantEntity : ITenantEntity, IAudited, ISoftDeletable
{
    protected TenantEntity()
        : this(Guid.CreateVersion7())
    {
    }

    protected TenantEntity(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; internal set; }

    public DateTimeOffset CreatedAt { get; internal set; }

    public Guid CreatedBy { get; internal set; }

    public Channel CreatedVia { get; internal set; }

    public DateTimeOffset UpdatedAt { get; internal set; }

    public Guid UpdatedBy { get; internal set; }

    public Channel UpdatedVia { get; internal set; }

    public DateTimeOffset? DeletedAt { get; internal set; }

    public Guid? DeletedBy { get; internal set; }

    public Guid? DeletionBatchId { get; internal set; }
}
