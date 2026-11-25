using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Api.Tests.Isolation;

/// <summary>
/// Isolamento no nível dos dados (docs/arquitetura.md §3.7, parte a): para <b>cada</b> tipo <see cref="ITenantEntity"/>
/// mapeado (descoberto por reflexão), o Espaço B não vê, não encontra nem altera linhas do Espaço A.
/// </summary>
public class TenantEntityIsolationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly System.Reflection.MethodInfo[] Helpers = typeof(TenantEntityIsolationTests)
        .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    /// <summary>Tipos <see cref="ITenantEntity"/> mapeados no contexto de teste (modelo real + sondas).</summary>
    private List<Type> MappedTenantEntities()
    {
        // O modelo é construído sem tocar no banco; entidades dependentes (owned) não têm Espaço próprio.
        using var db = postgres.CreateContext(new CurrentContext());
        return db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && typeof(ITenantEntity).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void Todo_tipo_ITenantEntity_mapeado_tem_fabrica_de_dados()
    {
        var mapped = MappedTenantEntities();

        mapped.ShouldNotBeEmpty("as sondas do contexto de teste são ITenantEntity; sem elas a suíte não exercita nada.");

        var semFabrica = mapped.Where(t => !TenantDataFactories.ByType.ContainsKey(t)).Select(t => t.Name).ToList();
        semFabrica.ShouldBeEmpty(
            $"Tipos ITenantEntity sem fábrica em {nameof(TenantDataFactories)}: {string.Join(", ", semFabrica)}. Acrescente a fábrica de dados do tipo.");
    }

    [Fact]
    public void Nao_ha_fabrica_para_tipo_que_nao_e_mais_mapeado()
    {
        var mapped = MappedTenantEntities().ToHashSet();

        var orfas = TenantDataFactories.ByType.Keys.Where(t => !mapped.Contains(t)).Select(t => t.Name).ToList();
        orfas.ShouldBeEmpty($"Fábricas de tipos que não são mais ITenantEntity mapeados: {string.Join(", ", orfas)}.");
    }

    [Fact]
    public async Task Espaco_B_nao_ve_nem_apaga_dados_do_Espaco_A_em_nenhum_tipo()
    {
        foreach (var type in MappedTenantEntities())
        {
            // Espaços novos por tipo: a fábrica de um tipo pode gravar linhas de outros (o pai de uma FK composta).
            var tenantA = Guid.CreateVersion7();
            var tenantB = Guid.CreateVersion7();
            var factory = TenantDataFactories.ByType[type];
            var idsA = await SeedAsync(tenantA, factory($"A-{type.Name}"), type);
            var idsB = await SeedAsync(tenantB, factory($"B-{type.Name}"), type);

            await using var asB = postgres.CreateContext(tenantB);

            var visibleToB = await Call<IReadOnlyList<Guid>>(nameof(VisibleIdsAsync), type, asB);
            visibleToB.ShouldBe(idsB, ignoreOrder: true, $"{type.Name}: B deve ver exatamente as próprias linhas.");
            visibleToB.ShouldNotContain(id => idsA.Contains(id), $"{type.Name}: B enxergou linha de A.");

            foreach (var idA in idsA)
            {
                (await Call<int>(nameof(DeleteByIdAsync), type, asB, idA)).ShouldBe(0, $"{type.Name}: B apagou a linha {idA} de A.");
            }

            await using var asA = postgres.CreateContext(tenantA);
            (await Call<IReadOnlyList<Guid>>(nameof(VisibleIdsAsync), type, asA)).ShouldBe(idsA, ignoreOrder: true, $"{type.Name}: as linhas de A mudaram.");
            (await Call<int>(nameof(RawCountAsync), type, asA, tenantA)).ShouldBe(idsA.Count);
        }
    }

    [Fact]
    public async Task Sem_Espaco_no_contexto_nenhum_tipo_retorna_linhas()
    {
        var tenant = Guid.CreateVersion7();

        foreach (var type in MappedTenantEntities())
        {
            await SeedAsync(tenant, TenantDataFactories.ByType[type]($"S-{type.Name}"), type);

            await using var semContexto = postgres.CreateContext(new CurrentContext());
            (await Call<IReadOnlyList<Guid>>(nameof(VisibleIdsAsync), type, semContexto))
                .ShouldBeEmpty($"{type.Name}: o filtro deve falhar fechado sem Espaço.");
        }
    }

    /// <summary>Grava os dados da fábrica no Espaço e devolve os ids das linhas do <paramref name="type"/>.</summary>
    private async Task<IReadOnlyList<Guid>> SeedAsync(Guid tenantId, IReadOnlyList<object> entities, Type type)
    {
        await using var db = postgres.CreateContext(tenantId);

        // As tabelas da 002 têm FK para tenants; as sondas não.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO tenants (id, name, onboarding_steps, created_at, created_by, created_via, updated_at, updated_by, updated_via)
            VALUES ({tenantId}, 'Espaço de teste', jsonb_build_object(), now(), {tenantId}, 'system', now(), {tenantId}, 'system')
            ON CONFLICT (id) DO NOTHING
            """);

        db.AddRange(entities);
        await db.SaveChangesAsync();

        return entities.Where(type.IsInstanceOfType).Cast<ITenantEntity>().Select(e => e.Id).ToList();
    }

    private static async Task<TResult> Call<TResult>(string helper, Type entityType, params object[] args)
    {
        var method = Helpers.Single(m => m.Name == helper).MakeGenericMethod(entityType);
        return await (Task<TResult>)method.Invoke(null, args)!;
    }

    private static async Task<IReadOnlyList<Guid>> VisibleIdsAsync<T>(DbContext db)
        where T : class, ITenantEntity =>
        await db.Set<T>().Select(e => e.Id).ToListAsync();

    private static Task<int> DeleteByIdAsync<T>(DbContext db, Guid id)
        where T : class, ITenantEntity =>
        db.Set<T>().Where(e => e.Id == id).ExecuteDeleteAsync();

    private static Task<int> RawCountAsync<T>(DbContext db, Guid tenantId)
        where T : class, ITenantEntity =>
        db.Set<T>().IgnoreQueryFilters().CountAsync(e => e.TenantId == tenantId);
}
