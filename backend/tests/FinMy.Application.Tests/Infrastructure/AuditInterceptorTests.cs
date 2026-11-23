using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Application.Tests.Infrastructure;

[Collection(PostgresTestGroup.Name)]
public class AuditInterceptorTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private async Task<ProbeParent> ReadAsync(Guid tenant, Guid id)
    {
        await using var db = postgres.CreateContext(tenant);
        return await db.Parents.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    [Fact]
    public async Task Entidade_nova_recebe_criacao_e_atualizacao_sozinhas()
    {
        var tenant = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenant, user, new FakeClock(T0));
        var parent = new ProbeParent { Name = "nova" };

        db.Add(parent);
        await db.SaveChangesAsync();

        var salva = await ReadAsync(tenant, parent.Id);
        salva.CreatedAt.ShouldBe(T0);
        salva.CreatedBy.ShouldBe(user);
        salva.CreatedVia.ShouldBe(Channel.App);
        salva.UpdatedAt.ShouldBe(T0);
        salva.UpdatedBy.ShouldBe(user);
        salva.UpdatedVia.ShouldBe(Channel.App);
    }

    [Fact]
    public async Task Atualizacao_muda_updated_e_preserva_created()
    {
        var tenant = Guid.CreateVersion7();
        var criador = Guid.CreateVersion7();
        var editor = Guid.CreateVersion7();
        Guid id;
        await using (var db = postgres.CreateContext(tenant, criador, new FakeClock(T0)))
        {
            var parent = new ProbeParent { Name = "v1" };
            db.Add(parent);
            await db.SaveChangesAsync();
            id = parent.Id;
        }

        var depois = T0.AddHours(3);
        var claude = new CurrentContext();
        claude.Populate(editor, tenant, Channel.Claude);
        await using (var db = postgres.CreateContext(claude, new FakeClock(depois)))
        {
            var parent = await db.Parents.SingleAsync(p => p.Id == id);
            parent.Name = "v2";
            await db.SaveChangesAsync();
        }

        var salva = await ReadAsync(tenant, id);
        salva.Name.ShouldBe("v2");
        salva.CreatedAt.ShouldBe(T0);
        salva.CreatedBy.ShouldBe(criador);
        salva.CreatedVia.ShouldBe(Channel.App);
        salva.UpdatedAt.ShouldBe(depois);
        salva.UpdatedBy.ShouldBe(editor);
        salva.UpdatedVia.ShouldBe(Channel.Claude);
    }

    [Fact]
    public async Task Created_nao_e_enviado_no_update_mesmo_se_alterado_na_memoria()
    {
        var tenant = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenant, user, new FakeClock(T0));
        var parent = new ProbeParent { Name = "v1" };
        db.Add(parent);
        await db.SaveChangesAsync();

        parent.Name = "v2";
        db.Entry(parent).Property(nameof(ProbeParent.CreatedBy)).CurrentValue = Guid.CreateVersion7();
        await db.SaveChangesAsync();

        (await ReadAsync(tenant, parent.Id)).CreatedBy.ShouldBe(user);
    }

    [Fact]
    public async Task Canal_do_contexto_vai_para_created_via()
    {
        var tenant = Guid.CreateVersion7();
        var contexto = new CurrentContext();
        contexto.Populate(Guid.CreateVersion7(), tenant, Channel.Claude);
        await using var db = postgres.CreateContext(contexto);
        var parent = new ProbeParent { Name = "via claude" };

        db.Add(parent);
        await db.SaveChangesAsync();

        var salva = await ReadAsync(tenant, parent.Id);
        salva.CreatedVia.ShouldBe(Channel.Claude);
        salva.UpdatedVia.ShouldBe(Channel.Claude);
    }

    [Fact]
    public async Task Escopo_de_sistema_grava_o_usuario_de_sistema()
    {
        var tenant = Guid.CreateVersion7();
        var contexto = new CurrentContext();
        await using var db = postgres.CreateContext(contexto);
        var parent = new ProbeParent { Name = "job" };

        using (contexto.BeginSystemScope(tenant))
        {
            db.Add(parent);
            await db.SaveChangesAsync();
        }

        var salva = await ReadAsync(tenant, parent.Id);
        salva.CreatedBy.ShouldBe(SystemUser.Id);
        salva.UpdatedBy.ShouldBe(SystemUser.Id);
        salva.CreatedVia.ShouldBe(Channel.System);
    }

    [Fact]
    public async Task Gravar_com_Espaco_mas_sem_usuario_nem_sistema_falha()
    {
        await using var db = postgres.CreateContext(new ContextoSemUsuario(Guid.CreateVersion7()));
        db.Add(new ProbeParent { Name = "anônimo" });

        await Should.ThrowAsync<MissingUserContextException>(() => db.SaveChangesAsync());
    }

    private sealed class ContextoSemUsuario(Guid tenantId) : ICurrentContext
    {
        public Guid? UserId => null;

        public Guid? TenantId => tenantId;

        public Channel Channel => Channel.App;

        public bool IsAuthenticated => false;

        public bool IsSystem => false;

        public Guid TenantIdOrEmpty => tenantId;

        public Guid DeletionBatchId { get; } = Guid.CreateVersion7();

        public Guid BeginDeletionBatch() => throw new NotSupportedException();

        public IDisposable BeginSystemScope(Guid tenantId) => throw new NotSupportedException();
    }
}
