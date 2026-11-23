using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Identity;
using FinMy.Domain.Monetary;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMy.Application.Tests.Infrastructure;

/// <summary>Migração <c>Wallets</c> (002 T003): tabelas, índices parciais, CHECKs e FKs compostas no Postgres real.</summary>
[Collection(PostgresTestGroup.Name)]
public class WalletsMigrationTests(PostgresFixture postgres)
{
    private static readonly DateOnly Hoje = new(2026, 3, 10);

    private sealed record Setup(string ConnectionString, Guid TenantId, Guid OtherTenantId);

    private static FinMyDbContext CreateContext(string connectionString, Guid tenantId)
    {
        var current = new CurrentContext();
        current.Populate(Guid.CreateVersion7(), tenantId, Channel.App);
        var builder = new DbContextOptionsBuilder<FinMyDbContext>();
        builder.UseFinMyNpgsql(connectionString).AddFinMyInterceptors(PostgresFixture.DefaultClock);
        return new FinMyDbContext(builder.Options, current);
    }

    private async Task<Setup> MigratedWithTwoTenantsAsync()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();

        var system = new CurrentContext();
        using var scope = system.BeginSystemScope(Guid.CreateVersion7());
        var builder = new DbContextOptionsBuilder<FinMyDbContext>();
        builder.UseFinMyNpgsql(connectionString).AddFinMyInterceptors(PostgresFixture.DefaultClock);
        await using var db = new FinMyDbContext(builder.Options, system);
        await db.Database.MigrateAsync();

        var tenant = new Tenant("Espaço de Lia");
        var other = new Tenant("Espaço de Léo");
        db.AddRange(tenant, other);
        await db.SaveChangesAsync();

        return new Setup(connectionString, tenant.Id, other.Id);
    }

    private static Wallet NewWallet(string name, bool primary = false) =>
        Wallet.Create(name, WalletType.Checking, "#1E88E5", Money.Zero, Hoje, primary);

    private static async Task<T> SavingAsync<T>(FinMyDbContext db, T entity)
        where T : class
    {
        db.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    private static async Task<string> ViolatedConstraintAsync(Func<Task> action)
    {
        var ex = await Should.ThrowAsync<Exception>(action);
        var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
        pg.ShouldNotBeNull();
        return pg.ConstraintName!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Migracao_Wallets_aplica_e_o_modelo_nao_tem_mudancas_pendentes()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);

        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(m => m.EndsWith("_Wallets"));
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task Persiste_e_le_carteira_lancamento_e_transferencia()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await SavingAsync(db, NewWallet("Azul", primary: true));
        var verde = await SavingAsync(db, NewWallet("Verde"));
        var entry = await SavingAsync(
            db,
            Entry.Create(EntryKind.Expense, "Tim", Money.FromCents(4499), Hoje, azul.Id, null, null, "plano", Hoje));
        var transfer = await SavingAsync(
            db,
            Transfer.Create(azul.Id, verde.Id, Money.FromCents(10_000), Hoje, null, null));

        await using var read = CreateContext(setup.ConnectionString, setup.TenantId);
        var savedEntry = await read.Set<Entry>().SingleAsync(e => e.Id == entry.Id);
        savedEntry.Kind.ShouldBe(EntryKind.Expense);
        savedEntry.Status.ShouldBe(EntryStatus.Settled);
        savedEntry.AmountCents.ShouldBe(4499);
        savedEntry.ReferenceMonth.ShouldBe(new DateOnly(2026, 3, 1));
        savedEntry.TenantId.ShouldBe(setup.TenantId);
        (await read.Set<Transfer>().SingleAsync(t => t.Id == transfer.Id)).Description.ShouldBe("Transferência");
        (await read.Set<Wallet>().SingleAsync(w => w.Id == azul.Id)).IsPrimary.ShouldBeTrue();

        var types = await read.Database.SqlQueryRaw<string>(
            "SELECT kind || '/' || status AS \"Value\" FROM entries").ToListAsync();
        types.ShouldBe(["expense/settled"]);
    }

    [Fact]
    public async Task Indice_unico_de_principal_recusa_duas_principais_ativas_no_mesmo_Espaco()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        await SavingAsync(db, NewWallet("Azul", primary: true));

        var constraint = await ViolatedConstraintAsync(() => SavingAsync(db, NewWallet("Verde", primary: true)));

        constraint.ShouldBe("ux_wallets_tenant_id_primary");
    }

    [Fact]
    public async Task Principal_excluida_ou_de_outro_Espaco_nao_conflita()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await SavingAsync(db, NewWallet("Azul", primary: true));
        await using var other = CreateContext(setup.ConnectionString, setup.OtherTenantId);
        await SavingAsync(other, NewWallet("Azul", primary: true));

        db.Remove(azul); // exclusão lógica
        await db.SaveChangesAsync();
        await SavingAsync(db, NewWallet("Verde", primary: true));

        (await db.Set<Wallet>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Carteira_principal_arquivada_e_recusada_pelo_banco()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await SavingAsync(db, NewWallet("Azul", primary: true));

        var constraint = await ViolatedConstraintAsync(() => ExecuteAsync(
            setup.ConnectionString,
            "UPDATE wallets SET archived_at = now() WHERE id = @id",
            ("id", azul.Id)));

        constraint.ShouldBe("ck_wallets_primary_not_archived");
    }

    [Fact]
    public async Task Nome_normalizado_e_unico_no_Espaco_entre_carteiras_nao_excluidas()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await SavingAsync(db, NewWallet("Açaí"));

        var constraint = await ViolatedConstraintAsync(() => SavingAsync(db, NewWallet("  AÇAI ")));
        constraint.ShouldBe("ix_wallets_tenant_id_name_normalized");
        db.ChangeTracker.Clear();

        // Outro Espaço pode usar o mesmo nome.
        await using var other = CreateContext(setup.ConnectionString, setup.OtherTenantId);
        await SavingAsync(other, NewWallet("Açaí"));

        // Depois da exclusão lógica o nome fica livre.
        db.Remove(await db.Set<Wallet>().SingleAsync(w => w.Id == azul.Id));
        await db.SaveChangesAsync();
        await SavingAsync(db, NewWallet("açai"));
    }

    [Theory]
    [InlineData("UPDATE wallets SET type = 'crypto'", "ck_wallets_type")]
    [InlineData("UPDATE wallets SET color = 'azul'", "ck_wallets_color")]
    [InlineData("UPDATE wallets SET initial_balance_cents = 1000000000", "ck_wallets_initial_balance_cents")]
    [InlineData("UPDATE wallets SET initial_balance_cents = -1000000000", "ck_wallets_initial_balance_cents")]
    [InlineData("UPDATE wallets SET initial_balance_date = DATE '1999-12-31'", "ck_wallets_initial_balance_date")]
    [InlineData("UPDATE entries SET kind = 'transfer'", "ck_entries_kind")]
    [InlineData("UPDATE entries SET status = 'done'", "ck_entries_status")]
    [InlineData("UPDATE entries SET amount_cents = 0", "ck_entries_amount_cents")]
    [InlineData("UPDATE entries SET amount_cents = 1000000000", "ck_entries_amount_cents")]
    [InlineData("UPDATE entries SET entry_date = DATE '2101-01-01'", "ck_entries_entry_date")]
    [InlineData("UPDATE entries SET reference_month = DATE '2026-03-02'", "ck_entries_reference_month")]
    [InlineData("UPDATE entries SET reference_month = DATE '1999-12-01'", "ck_entries_reference_month")]
    [InlineData("UPDATE transfers SET from_wallet_id = to_wallet_id", "ck_transfers_different_wallets")]
    [InlineData("UPDATE transfers SET amount_cents = 0", "ck_transfers_amount_cents")]
    [InlineData("UPDATE transfers SET transfer_date = DATE '1999-12-31'", "ck_transfers_transfer_date")]
    public async Task Banco_recusa_valores_fora_das_regras(string update, string expectedConstraint)
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await SavingAsync(db, NewWallet("Azul", primary: true));
        var verde = await SavingAsync(db, NewWallet("Verde"));
        await SavingAsync(db, Entry.Create(EntryKind.Income, "Salário", Money.FromCents(100_000), Hoje, azul.Id, null, null, null, Hoje));
        await SavingAsync(db, Transfer.Create(azul.Id, verde.Id, Money.FromCents(500), Hoje, null, null));

        var constraint = await ViolatedConstraintAsync(() => ExecuteAsync(setup.ConnectionString, update));

        constraint.ShouldBe(expectedConstraint);
    }

    [Fact]
    public async Task Transferencia_para_a_mesma_carteira_e_recusada_pelo_banco()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await SavingAsync(db, NewWallet("Azul", primary: true));

        const string sql = """
            INSERT INTO transfers (id, tenant_id, from_wallet_id, to_wallet_id, amount_cents, transfer_date, description,
                description_normalized, created_at, created_by, created_via, updated_at, updated_by, updated_via)
            VALUES (gen_random_uuid(), @t, @w, @w, 100, DATE '2026-03-10', 'Transferência', 'transferencia',
                now(), gen_random_uuid(), 'app', now(), gen_random_uuid(), 'app')
            """;
        var constraint = await ViolatedConstraintAsync(() => ExecuteAsync(
            setup.ConnectionString, sql, ("t", setup.TenantId), ("w", azul.Id)));

        constraint.ShouldBe("ck_transfers_different_wallets");
    }

    [Fact]
    public async Task Fks_compostas_recusam_carteira_de_outro_Espaco()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var other = CreateContext(setup.ConnectionString, setup.OtherTenantId);
        var carteiraDeLeo = await SavingAsync(other, NewWallet("Azul", primary: true));
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var carteiraDeLia = await SavingAsync(db, NewWallet("Azul", primary: true));

        var entryConstraint = await ViolatedConstraintAsync(() => SavingAsync(
            db,
            Entry.Create(EntryKind.Expense, "Tim", Money.FromCents(100), Hoje, carteiraDeLeo.Id, null, null, null, Hoje)));
        db.ChangeTracker.Clear();
        var transferFrom = await ViolatedConstraintAsync(() => SavingAsync(
            db,
            Transfer.Create(carteiraDeLeo.Id, carteiraDeLia.Id, Money.FromCents(100), Hoje, null, null)));
        db.ChangeTracker.Clear();
        var transferTo = await ViolatedConstraintAsync(() => SavingAsync(
            db,
            Transfer.Create(carteiraDeLia.Id, carteiraDeLeo.Id, Money.FromCents(100), Hoje, null, null)));

        entryConstraint.ShouldBe("fk_entries_wallet_tenant_id_wallet_id");
        transferFrom.ShouldBe("fk_transfers_wallet_tenant_id_from_wallet_id");
        transferTo.ShouldBe("fk_transfers_wallet_tenant_id_to_wallet_id");
    }

    [Fact]
    public async Task Tem_os_indices_do_modelo_de_dados()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND tablename IN ('wallets', 'entries', 'transfers')")
            .ToListAsync();

        indexes.ShouldContain("ix_entries_tenant_id_description_normalized");
        indexes.ShouldContain("ix_entries_tenant_id_reference_month");
        indexes.ShouldContain("ix_entries_tenant_id_status_reference_month");
        indexes.ShouldContain("ix_entries_tenant_id_wallet_id");
        indexes.ShouldContain("ix_transfers_tenant_id_transfer_date");
        indexes.ShouldContain("ix_wallets_tenant_id_name_normalized");
        indexes.ShouldContain("ux_wallets_tenant_id_primary");
    }
}
