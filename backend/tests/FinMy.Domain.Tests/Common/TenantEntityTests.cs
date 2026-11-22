using System.Reflection;
using FinMy.Domain.Common;

namespace FinMy.Domain.Tests.Common;

public class TenantEntityTests
{
    private sealed class Sonda : TenantEntity;

    [Fact]
    public void Id_e_gerado_como_uuid_v7_e_unico()
    {
        var a = new Sonda();
        var b = new Sonda();

        a.Id.ShouldNotBe(Guid.Empty);
        a.Id.ShouldNotBe(b.Id);
        a.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Entidade_nova_nao_tem_espaco_nem_auditoria_nem_exclusao()
    {
        var e = new Sonda();

        e.TenantId.ShouldBe(Guid.Empty);
        e.CreatedBy.ShouldBe(Guid.Empty);
        e.DeletedAt.ShouldBeNull();
        e.DeletedBy.ShouldBeNull();
        e.DeletionBatchId.ShouldBeNull();
    }

    [Fact]
    public void Implementa_as_interfaces_de_marcacao()
    {
        var e = new Sonda();

        e.ShouldBeAssignableTo<ITenantEntity>();
        e.ShouldBeAssignableTo<IEntity>();
        e.ShouldBeAssignableTo<IAudited>();
        e.ShouldBeAssignableTo<ISoftDeletable>();
        e.ShouldNotBeAssignableTo<IArchivable>();
    }

    [Theory]
    [InlineData(nameof(TenantEntity.Id))]
    [InlineData(nameof(TenantEntity.TenantId))]
    [InlineData(nameof(TenantEntity.CreatedAt))]
    [InlineData(nameof(TenantEntity.CreatedBy))]
    [InlineData(nameof(TenantEntity.CreatedVia))]
    [InlineData(nameof(TenantEntity.UpdatedAt))]
    [InlineData(nameof(TenantEntity.UpdatedBy))]
    [InlineData(nameof(TenantEntity.UpdatedVia))]
    [InlineData(nameof(TenantEntity.DeletedAt))]
    [InlineData(nameof(TenantEntity.DeletedBy))]
    [InlineData(nameof(TenantEntity.DeletionBatchId))]
    public void Setters_nao_sao_publicos(string propriedade)
    {
        var setter = typeof(TenantEntity).GetProperty(propriedade)!.SetMethod;

        setter.ShouldNotBeNull();
        setter.IsPublic.ShouldBeFalse();
    }

    [Fact]
    public void Interceptors_conseguem_preencher_via_internal()
    {
        var tenant = Guid.CreateVersion7();
        var agora = DateTimeOffset.UtcNow;

        var e = new Sonda
        {
            TenantId = tenant,
            CreatedAt = agora,
            CreatedBy = SystemUser.Id,
            CreatedVia = Channel.System,
        };

        e.TenantId.ShouldBe(tenant);
        e.CreatedVia.ShouldBe(Channel.System);
    }

    [Fact]
    public void Interfaces_nao_expoem_setters()
    {
        foreach (var tipo in new[] { typeof(IEntity), typeof(ITenantEntity), typeof(IAudited), typeof(ISoftDeletable), typeof(IArchivable) })
        {
            tipo.GetProperties().ShouldAllBe(p => p.SetMethod == null, tipo.Name);
        }
    }
}
