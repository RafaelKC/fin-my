using System.Linq.Expressions;
using FinMy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMy.Infrastructure.Persistence;

public static class TenantRelationshipExtensions
{
    /// <summary>
    /// Torna a chave estrangeira composta: <c>(tenant_id, x_id)</c> → <c>x (tenant_id, id)</c>
    /// (docs/arquitetura.md §3.5). O banco recusa referência entre Espaços.
    /// Exclusão em cascata não é usada (exclusão é sempre lógica).
    /// </summary>
    public static ReferenceCollectionBuilder<TPrincipal, TDependent> HasTenantForeignKey<TPrincipal, TDependent>(
        this ReferenceCollectionBuilder<TPrincipal, TDependent> relationship,
        Expression<Func<TDependent, Guid>> foreignKey)
        where TPrincipal : class, ITenantEntity
        where TDependent : class, ITenantEntity
    {
        if (foreignKey.Body is not MemberExpression member)
        {
            throw new ArgumentException("Informe a propriedade da chave estrangeira (e => e.XId).", nameof(foreignKey));
        }

        return relationship
            .HasForeignKey(nameof(ITenantEntity.TenantId), member.Member.Name)
            .HasPrincipalKey(nameof(ITenantEntity.TenantId), nameof(IEntity.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}
