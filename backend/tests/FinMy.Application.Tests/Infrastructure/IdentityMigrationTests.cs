using FinMy.Domain.Identity;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMy.Application.Tests.Infrastructure;

/// <summary>Aplica as migrações do contexto real num Postgres vazio e confere o esquema de identidade.</summary>
[Collection(PostgresTestGroup.Name)]
public class IdentityMigrationTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = PostgresFixture.DefaultClock.UtcNow;

    private static FinMyDbContext CreateContext(string connectionString, CurrentContext? current = null)
    {
        var builder = new DbContextOptionsBuilder<FinMyDbContext>();
        builder.UseFinMyNpgsql(connectionString).AddFinMyInterceptors(PostgresFixture.DefaultClock);
        return new FinMyDbContext(builder.Options, current ?? new CurrentContext());
    }

    private async Task<string> MigratedDatabaseAsync()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var db = CreateContext(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    private static async Task<List<string>> QueryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    [Fact]
    public async Task Migracoes_aplicam_num_banco_vazio_e_o_modelo_nao_tem_mudancas_pendentes()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var db = CreateContext(connectionString);

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(m => m.EndsWith("_Initial"));
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task Cria_as_tabelas_globais_e_as_do_Espaco_com_nomes_em_snake_case()
    {
        var connectionString = await MigratedDatabaseAsync();

        var tables = await QueryAsync(
            connectionString,
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory' ORDER BY 1");

        tables.ShouldBe(
            [
                "card_purchase_tags", "card_purchases", "credit_cards", "entries", "entry_tags", "idempotency_keys", "installment_plans",
                "invoice_payments", "invoices", "login_throttles", "oauth_authorization_usage", "oauth_consent_tickets", "openiddict_applications", "openiddict_authorizations", "openiddict_scopes", "openiddict_tokens", "password_reset_tokens", "refresh_tokens", "tags", "tenant_members",
                "tenants", "transfers", "users", "wallets",
            ]);
    }

    [Theory]
    [InlineData("users", "id,name,email,email_normalized,password_hash,security_stamp,created_at,updated_at")]
    [InlineData("tenants", "id,name,onboarding_steps,onboarding_dismissed_at,created_at,created_by,created_via,updated_at,updated_by,updated_via")]
    [InlineData("tenant_members", "tenant_id,user_id,role,joined_at")]
    [InlineData("refresh_tokens", "id,user_id,family_id,token_hash,device_label,created_at,last_used_at,expires_at,revoked_at,revoked_reason,replaced_by_id")]
    [InlineData("password_reset_tokens", "id,user_id,token_hash,created_at,expires_at,used_at")]
    [InlineData("login_throttles", "email_normalized,failed_count,window_started_at,locked_until")]
    public async Task Tabela_tem_exatamente_as_colunas_do_modelo_de_dados(string table, string columns)
    {
        var connectionString = await MigratedDatabaseAsync();

        var actual = await QueryAsync(
            connectionString,
            $"SELECT column_name FROM information_schema.columns WHERE table_name = '{table}' ORDER BY ordinal_position");

        actual.Order().ShouldBe(columns.Split(',').Order());
    }

    [Fact]
    public async Task Colunas_tem_tipos_e_nulidade_do_modelo_de_dados()
    {
        var connectionString = await MigratedDatabaseAsync();

        var columns = await QueryAsync(
            connectionString,
            """
            SELECT table_name || '.' || column_name || ':' || data_type || ':' || coalesce(character_maximum_length::text, '') || ':' || is_nullable
            FROM information_schema.columns WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
            """);

        columns.ShouldContain("users.name:character varying:60:NO");
        columns.ShouldContain("users.email:character varying:254:NO");
        columns.ShouldContain("users.email_normalized:character varying:254:NO");
        columns.ShouldContain("users.password_hash:text::NO");
        columns.ShouldContain("users.security_stamp:uuid::NO");
        columns.ShouldContain("users.created_at:timestamp with time zone::NO");
        columns.ShouldContain("tenants.name:character varying:60:NO");
        columns.ShouldContain("tenants.onboarding_steps:jsonb::NO");
        columns.ShouldContain("tenants.onboarding_dismissed_at:timestamp with time zone::YES");
        columns.ShouldContain("tenants.created_via:text::NO");
        columns.ShouldContain("tenant_members.role:text::NO");
        columns.ShouldContain("refresh_tokens.token_hash:bytea::NO");
        columns.ShouldContain("refresh_tokens.device_label:character varying:120:YES");
        columns.ShouldContain("refresh_tokens.revoked_at:timestamp with time zone::YES");
        columns.ShouldContain("refresh_tokens.revoked_reason:text::YES");
        columns.ShouldContain("refresh_tokens.replaced_by_id:uuid::YES");
        columns.ShouldContain("password_reset_tokens.token_hash:bytea::NO");
        columns.ShouldContain("password_reset_tokens.used_at:timestamp with time zone::YES");
        columns.ShouldContain("login_throttles.email_normalized:character varying:254:NO");
        columns.ShouldContain("login_throttles.failed_count:integer::NO");
        columns.ShouldContain("login_throttles.locked_until:timestamp with time zone::YES");
    }

    [Fact]
    public async Task Onboarding_do_espaco_tem_padrao_vazio_no_banco()
    {
        var connectionString = await MigratedDatabaseAsync();

        var defaults = await QueryAsync(
            connectionString,
            "SELECT column_default FROM information_schema.columns WHERE table_name = 'tenants' AND column_name = 'onboarding_steps'");

        defaults.Single().ShouldContain("'{}'::jsonb");
    }

    [Fact]
    public async Task E_mail_normalizado_duplicado_e_recusado()
    {
        var connectionString = await MigratedDatabaseAsync();
        await using var db = CreateContext(connectionString);
        db.Add(new User("Maria", "Maria@Exemplo.com", "hash", Now));
        await db.SaveChangesAsync();

        await using var other = CreateContext(connectionString);
        other.Add(new User("Outra", "  MARIA@exemplo.com", "hash", Now));

        var ex = await Should.ThrowAsync<DbUpdateException>(() => other.SaveChangesAsync());
        ((PostgresException)ex.InnerException!).ConstraintName.ShouldBe("ix_users_email_normalized");
    }

    [Fact]
    public async Task Usuario_nao_pode_participar_de_dois_Espacos_nem_Espaco_ter_dois_participantes()
    {
        var connectionString = await MigratedDatabaseAsync();
        var system = new CurrentContext();
        using var scope = system.BeginSystemScope(Guid.CreateVersion7());
        await using var db = CreateContext(connectionString, system);

        var maria = new User("Maria", "maria@exemplo.com", "hash", Now);
        var joao = new User("João", "joao@exemplo.com", "hash", Now);
        var tenantA = new Tenant("A");
        var tenantB = new Tenant("B");
        db.AddRange(maria, joao, tenantA, tenantB, new TenantMember(tenantA.Id, maria.Id, Now));
        await db.SaveChangesAsync();

        db.Add(new TenantMember(tenantB.Id, maria.Id, Now));
        var segundoEspaco = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ((PostgresException)segundoEspaco.InnerException!).ConstraintName.ShouldBe("ix_tenant_members_user_id");
        db.ChangeTracker.Clear();

        db.Add(new TenantMember(tenantA.Id, joao.Id, Now));
        var segundoParticipante = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ((PostgresException)segundoParticipante.InnerException!).ConstraintName.ShouldBe("ix_tenant_members_tenant_id");
    }

    [Fact]
    public async Task Papel_fora_da_lista_e_recusado_pelo_banco()
    {
        var connectionString = await MigratedDatabaseAsync();
        var system = new CurrentContext();
        using var scope = system.BeginSystemScope(Guid.CreateVersion7());
        await using var db = CreateContext(connectionString, system);
        var user = new User("Maria", "maria@exemplo.com", "hash", Now);
        var tenant = new Tenant("A");
        db.AddRange(user, tenant);
        await db.SaveChangesAsync();

        var ex = await Should.ThrowAsync<PostgresException>(async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "INSERT INTO tenant_members (tenant_id, user_id, role, joined_at) VALUES (@t, @u, 'admin', now())", connection);
            command.Parameters.AddWithValue("t", tenant.Id);
            command.Parameters.AddWithValue("u", user.Id);
            await command.ExecuteNonQueryAsync();
        });

        ex.ConstraintName.ShouldBe("ck_tenant_members_role");
    }

    [Fact]
    public async Task Hash_do_refresh_token_e_do_token_de_redefinicao_sao_unicos()
    {
        var connectionString = await MigratedDatabaseAsync();
        await using var db = CreateContext(connectionString);
        var user = new User("Maria", "maria@exemplo.com", "hash", Now);
        var hash = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        db.AddRange(
            user,
            new RefreshToken(user.Id, Guid.CreateVersion7(), hash, null, Now, Now.AddDays(90)),
            new PasswordResetToken(user.Id, hash, Now, Now.AddHours(1)));
        await db.SaveChangesAsync();

        db.Add(new RefreshToken(user.Id, Guid.CreateVersion7(), hash, null, Now, Now.AddDays(90)));
        var refresh = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ((PostgresException)refresh.InnerException!).ConstraintName.ShouldBe("ix_refresh_tokens_token_hash");
        db.ChangeTracker.Clear();

        db.Add(new PasswordResetToken(user.Id, hash, Now, Now.AddHours(1)));
        var reset = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ((PostgresException)reset.InnerException!).ConstraintName.ShouldBe("ix_password_reset_tokens_token_hash");
    }

    [Fact]
    public async Task Motivos_de_revogacao_validos_sao_gravados_em_snake_case_e_outros_sao_recusados()
    {
        var connectionString = await MigratedDatabaseAsync();
        await using var db = CreateContext(connectionString);
        var user = new User("Maria", "maria@exemplo.com", "hash", Now);
        db.Add(user);
        await db.SaveChangesAsync();

        foreach (var reason in Enum.GetValues<RefreshTokenRevocationReason>())
        {
            var token = new RefreshToken(user.Id, Guid.CreateVersion7(), Guid.NewGuid().ToByteArray(), null, Now, Now.AddDays(90));
            token.GetType().GetProperty(nameof(RefreshToken.RevokedReason))!.SetValue(token, reason);
            db.Add(token);
        }

        await db.SaveChangesAsync();

        (await QueryAsync(connectionString, "SELECT revoked_reason FROM refresh_tokens ORDER BY 1"))
            .ShouldBe(["logout", "password_changed", "password_reset", "reuse_detected", "rotated"]);

        var ex = await Should.ThrowAsync<PostgresException>(async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "UPDATE refresh_tokens SET revoked_reason = 'expirado' WHERE id = (SELECT id FROM refresh_tokens LIMIT 1)", connection);
            await command.ExecuteNonQueryAsync();
        });
        ex.ConstraintName.ShouldBe("ck_refresh_tokens_revoked_reason");
    }

    [Fact]
    public async Task Controle_de_tentativas_usa_o_email_normalizado_como_chave_primaria()
    {
        var connectionString = await MigratedDatabaseAsync();
        await using var db = CreateContext(connectionString);
        db.Add(new LoginThrottle("maria@exemplo.com", Now));
        await db.SaveChangesAsync();

        await using var other = CreateContext(connectionString);
        other.Add(new LoginThrottle("maria@exemplo.com", Now));

        var ex = await Should.ThrowAsync<DbUpdateException>(() => other.SaveChangesAsync());
        ((PostgresException)ex.InnerException!).SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Espaco_e_gravado_com_auditoria_e_onboarding_vazio_e_lido_de_volta()
    {
        var connectionString = await MigratedDatabaseAsync();
        var author = Guid.CreateVersion7();
        var context = new CurrentContext();
        context.Populate(author, Guid.CreateVersion7(), Domain.Common.Channel.App);

        Guid id;
        await using (var db = CreateContext(connectionString, context))
        {
            var tenant = new Tenant("Finanças de Maria");
            db.Add(tenant);
            await db.SaveChangesAsync();
            id = tenant.Id;
        }

        await using var read = CreateContext(connectionString);
        var saved = await read.Set<Tenant>().SingleAsync(t => t.Id == id);
        saved.CreatedBy.ShouldBe(author);
        saved.CreatedAt.ShouldBe(Now);
        saved.CreatedVia.ShouldBe(Domain.Common.Channel.App);
        saved.OnboardingSteps.ShouldBeEmpty();
    }
}
