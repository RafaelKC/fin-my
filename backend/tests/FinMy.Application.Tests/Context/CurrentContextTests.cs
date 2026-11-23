using FinMy.Domain.Common;
using FinMy.Infrastructure.Context;

namespace FinMy.Application.Tests.Context;

public class CurrentContextTests
{
    [Fact]
    public void Sem_Espaco_TenantIdOrEmpty_e_Guid_vazio()
    {
        var ctx = new CurrentContext();

        ctx.TenantId.ShouldBeNull();
        ctx.TenantIdOrEmpty.ShouldBe(Guid.Empty);
        ctx.IsAuthenticated.ShouldBeFalse();
        ctx.IsSystem.ShouldBeFalse();
    }

    [Fact]
    public void Populate_define_usuario_espaco_e_canal()
    {
        var ctx = new CurrentContext();
        var user = Guid.NewGuid();
        var tenant = Guid.NewGuid();

        ctx.Populate(user, tenant, Channel.Claude);

        ctx.UserId.ShouldBe(user);
        ctx.TenantIdOrEmpty.ShouldBe(tenant);
        ctx.Channel.ShouldBe(Channel.Claude);
        ctx.IsAuthenticated.ShouldBeTrue();
        ctx.IsSystem.ShouldBeFalse();
    }

    [Fact]
    public void BeginSystemScope_usa_SystemUser_canal_system_e_restaura_ao_descartar()
    {
        var ctx = new CurrentContext();
        var user = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var other = Guid.NewGuid();
        ctx.Populate(user, tenant, Channel.App);

        using (ctx.BeginSystemScope(other))
        {
            ctx.IsSystem.ShouldBeTrue();
            ctx.IsAuthenticated.ShouldBeFalse();
            ctx.UserId.ShouldBe(SystemUser.Id);
            ctx.TenantIdOrEmpty.ShouldBe(other);
            ctx.Channel.ShouldBe(Channel.System);
        }

        ctx.IsSystem.ShouldBeFalse();
        ctx.UserId.ShouldBe(user);
        ctx.TenantIdOrEmpty.ShouldBe(tenant);
        ctx.Channel.ShouldBe(Channel.App);
    }

    [Fact]
    public void BeginSystemScope_sem_contexto_previo_volta_a_vazio()
    {
        var ctx = new CurrentContext();

        ctx.BeginSystemScope(Guid.NewGuid()).Dispose();

        ctx.TenantId.ShouldBeNull();
        ctx.UserId.ShouldBeNull();
        ctx.IsSystem.ShouldBeFalse();
    }

    [Fact]
    public void Escopos_aninhados_restauram_na_ordem_e_Dispose_duplo_e_inofensivo()
    {
        var ctx = new CurrentContext();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var outer = ctx.BeginSystemScope(a);
        var inner = ctx.BeginSystemScope(b);
        ctx.TenantIdOrEmpty.ShouldBe(b);

        inner.Dispose();
        inner.Dispose();
        ctx.TenantIdOrEmpty.ShouldBe(a);

        outer.Dispose();
        ctx.TenantIdOrEmpty.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void DeletionBatchId_e_estavel_na_instancia_e_diferente_entre_instancias()
    {
        var ctx = new CurrentContext();

        ctx.DeletionBatchId.ShouldNotBe(Guid.Empty);
        ctx.DeletionBatchId.ShouldBe(ctx.DeletionBatchId);
        new CurrentContext().DeletionBatchId.ShouldNotBe(ctx.DeletionBatchId);
    }

    [Fact]
    public void BeginDeletionBatch_abre_um_novo_lote_UUIDv7_e_o_torna_o_corrente()
    {
        var ctx = new CurrentContext();
        var primeiro = ctx.DeletionBatchId;

        var lote = ctx.BeginDeletionBatch();

        lote.ShouldNotBe(primeiro);
        lote.ShouldNotBe(Guid.Empty);
        lote.Version.ShouldBe(7);
        ctx.DeletionBatchId.ShouldBe(lote);
        ctx.BeginDeletionBatch().ShouldNotBe(lote);
    }
}
