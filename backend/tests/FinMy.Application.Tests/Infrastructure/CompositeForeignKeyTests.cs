using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMy.Application.Tests.Infrastructure;

[Collection(PostgresTestGroup.Name)]
public class CompositeForeignKeyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Filho_do_mesmo_Espaco_e_aceito()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenant);
        var parent = db.InTenant(new ProbeParent { Name = "pai" }, tenant);
        var item = db.InTenant(new ProbeItem { ParentId = parent.Id, Label = "filho" }, tenant);
        db.AddRange(parent, item);

        await db.SaveChangesAsync();

        (await db.Items.CountAsync(i => i.Id == item.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Banco_recusa_filho_do_Espaco_A_apontando_para_pai_do_Espaco_B()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        Guid parentOfB;
        await using (var dbB = postgres.CreateContext(tenantB))
        {
            var parentB = dbB.InTenant(new ProbeParent { Name = "pai de B" }, tenantB);
            dbB.Add(parentB);
            await dbB.SaveChangesAsync();
            parentOfB = parentB.Id;
        }

        await using var dbA = postgres.CreateContext(tenantA);
        var item = dbA.InTenant(new ProbeItem { ParentId = parentOfB, Label = "intruso" }, tenantA);
        dbA.Add(item);

        var ex = await Should.ThrowAsync<DbUpdateException>(() => dbA.SaveChangesAsync());

        var pg = ex.InnerException.ShouldBeOfType<PostgresException>();
        pg.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Pai_tem_unique_tenant_id_id()
    {
        await using var db = postgres.CreateContext(Guid.CreateVersion7());

        var count = await db.Database.SqlQueryRaw<int>(
            """
            SELECT count(*)::int AS "Value"
            FROM pg_constraint c
            WHERE c.conrelid = 'probe_parents'::regclass
              AND c.contype = 'u'
              AND (SELECT array_agg(a.attname::text ORDER BY k.ord)
                   FROM unnest(c.conkey) WITH ORDINALITY k(attnum, ord)
                   JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum) = ARRAY['tenant_id', 'id']
            """).SingleAsync();

        count.ShouldBe(1);
    }
}
