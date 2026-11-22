using System.Linq.Expressions;
using FinMy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinMy.Infrastructure.Persistence;

public static class ModelBuilderExtensions
{
    public const string TenantFilter = "Tenant";
    public const string SoftDeleteFilter = "SoftDelete";

    private static readonly ValueConverter<Channel, string> ChannelConverter = new(
        v => v.ToString().ToLowerInvariant(),
        v => Enum.Parse<Channel>(v, true));

    /// <summary>
    /// Aplica as convenções do Fin-My a todas as entidades já registradas no modelo
    /// (docs/arquitetura.md §3.3 e §3.5): filtros nomeados <c>Tenant</c> e <c>SoftDelete</c>,
    /// <c>UNIQUE (tenant_id, id)</c>, <c>xmin</c> como token de concorrência e canais em texto.
    /// Deve ser chamada depois de registrar as entidades e suas configurações.
    /// </summary>
    public static ModelBuilder ApplyFinMyConventions(this ModelBuilder modelBuilder, FinMyDbContext context)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.BaseType is null)
            .ToList();

        foreach (var entityType in entityTypes)
        {
            var clrType = entityType.ClrType;
            var entity = modelBuilder.Entity(clrType);

            if (typeof(ITenantEntity).IsAssignableFrom(clrType))
            {
                entity.HasQueryFilter(TenantFilter, BuildTenantFilter(clrType, context));
                entity.HasAlternateKey(nameof(ITenantEntity.TenantId), nameof(IEntity.Id));
                entity.Property(nameof(IEntity.Id)).ValueGeneratedNever();
                entity.Property<uint>("xmin").IsRowVersion();
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                entity.HasQueryFilter(SoftDeleteFilter, BuildSoftDeleteFilter(clrType));
            }

            if (typeof(IAudited).IsAssignableFrom(clrType))
            {
                entity.Property(nameof(IAudited.CreatedVia)).HasConversion(ChannelConverter);
                entity.Property(nameof(IAudited.UpdatedVia)).HasConversion(ChannelConverter);
            }
        }

        return modelBuilder;
    }

    // e => e.TenantId == context.TenantIdOrEmpty (o contexto é substituído pelo EF a cada consulta).
    private static LambdaExpression BuildTenantFilter(Type clrType, FinMyDbContext context)
    {
        var e = Expression.Parameter(clrType, "e");
        var body = Expression.Equal(
            Expression.Property(e, nameof(ITenantEntity.TenantId)),
            Expression.Property(Expression.Constant(context), nameof(FinMyDbContext.TenantIdOrEmpty)));

        return Expression.Lambda(body, e);
    }

    // e => e.DeletedAt == null
    private static LambdaExpression BuildSoftDeleteFilter(Type clrType)
    {
        var e = Expression.Parameter(clrType, "e");
        var body = Expression.Equal(
            Expression.Property(e, nameof(ISoftDeletable.DeletedAt)),
            Expression.Constant(null, typeof(DateTimeOffset?)));

        return Expression.Lambda(body, e);
    }
}
