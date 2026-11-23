using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;
using FinMy.Domain.Monetary;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Dinheiro = FinMy.Domain.Monetary.Money;

namespace FinMy.Application.Tests.Infrastructure;

/// <summary>Migração <c>CreditCards</c> (003 T003): tabelas, FK tripla, CHECKs, índices e <c>InvoiceRepository.GetOrCreate</c> no Postgres real.</summary>
[Collection(PostgresTestGroup.Name)]
public class CreditCardsMigrationTests(PostgresFixture postgres)
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

    private static async Task<T> SavingAsync<T>(FinMyDbContext db, T entity)
        where T : class
    {
        db.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    private static async Task<Wallet> NewWalletAsync(FinMyDbContext db, string name = "Azul") =>
        await SavingAsync(db, Wallet.Create(name, WalletType.Checking, "#1E88E5", Dinheiro.Zero, Hoje));

    private static async Task<CreditCard> NewCardAsync(FinMyDbContext db, Guid walletId, string name = "Verde", int closing = 5, int due = 15) =>
        await SavingAsync(db, CreditCard.Create(name, "#43A047", closing, due, walletId));

    private static Invoice NewInvoice(Guid cardId, int month = 3) =>
        Invoice.Create(cardId, YearMonth.Of(2026, month), new DateOnly(2026, month, 5), new DateOnly(2026, month, 15));

    private static CardPurchase NewPurchase(Guid cardId, Guid invoiceId, string description = "Mercado") =>
        CardPurchase.CreateManual(cardId, invoiceId, description, Dinheiro.FromCents(4_500), PurchaseNature.Debit, Hoje, null);

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

    /// <summary>Banco com um cartão, uma fatura, uma compra à vista, um plano com uma parcela e um pagamento.</summary>
    private async Task<Setup> SeededAsync()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await NewWalletAsync(db);
        var verde = await NewCardAsync(db, azul.Id);
        var invoice = await SavingAsync(db, NewInvoice(verde.Id));
        await SavingAsync(db, NewPurchase(verde.Id, invoice.Id));
        var plan = await SavingAsync(db, InstallmentPlan.Create(
            verde.Id, "Geladeira", InstallmentMode.TotalAmount, 10, 1, Dinheiro.FromCents(100_000), Hoje, YearMonth.Of(2026, 3)));
        await SavingAsync(db, CardPurchase.CreateInstallment(verde.Id, invoice.Id, plan, 1, Dinheiro.FromCents(10_000), null));
        await SavingAsync(db, InvoicePayment.Create(invoice.Id, azul.Id, Dinheiro.FromCents(1_000), Hoje));
        return setup;
    }

    [Fact]
    public async Task Migracao_CreditCards_aplica_e_o_modelo_nao_tem_mudancas_pendentes()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);

        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(m => m.EndsWith("_CreditCards"));
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task Persiste_e_le_cartao_fatura_compra_parcela_e_pagamento()
    {
        var setup = await SeededAsync();

        await using var read = CreateContext(setup.ConnectionString, setup.TenantId);
        var card = await read.Set<CreditCard>().SingleAsync();
        card.Name.ShouldBe("Verde");
        card.ClosingDay.ShouldBe(5);
        card.DueDay.ShouldBe(15);
        card.LimitCents.ShouldBeNull();
        var invoice = await read.Set<Invoice>().SingleAsync();
        invoice.DueMonth.ShouldBe(new DateOnly(2026, 3, 1));
        invoice.CreditCardId.ShouldBe(card.Id);
        var purchases = await read.Set<CardPurchase>().OrderBy(p => p.Origin).ToListAsync();
        purchases.Select(p => p.DisplayDescription).Order().ShouldBe(["Geladeira 1/10", "Mercado"]);
        purchases.ShouldAllBe(p => p.InvoiceId == invoice.Id && p.TenantId == setup.TenantId);
        (await read.Set<InstallmentPlan>().SingleAsync()).Mode.ShouldBe(InstallmentMode.TotalAmount);
        (await read.Set<InvoicePayment>().SingleAsync()).AmountCents.ShouldBe(1_000);

        var texts = await read.Database.SqlQueryRaw<string>(
            "SELECT nature || '/' || origin AS \"Value\" FROM card_purchases ORDER BY origin").ToListAsync();
        texts.ShouldBe(["debit/installment", "debit/manual"]);
    }

    [Fact]
    public async Task Compra_nao_pode_apontar_para_fatura_de_outro_cartao()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await NewWalletAsync(db);
        var verde = await NewCardAsync(db, azul.Id, "Verde");
        var carla = await NewCardAsync(db, azul.Id, "Carla");
        var faturaDeCarla = await SavingAsync(db, NewInvoice(carla.Id));
        var faturaDeVerde = await SavingAsync(db, NewInvoice(verde.Id));

        var criando = await ViolatedConstraintAsync(() => SavingAsync(db, NewPurchase(verde.Id, faturaDeCarla.Id)));
        db.ChangeTracker.Clear();

        var compra = await SavingAsync(db, NewPurchase(verde.Id, faturaDeVerde.Id));
        var movendo = await ViolatedConstraintAsync(() => ExecuteAsync(
            setup.ConnectionString,
            "UPDATE card_purchases SET invoice_id = @invoice WHERE id = @id",
            ("invoice", faturaDeCarla.Id),
            ("id", compra.Id)));

        criando.ShouldBe("fk_card_purchases_invoice_tenant_id_invoice_id_credit_card_id");
        movendo.ShouldBe("fk_card_purchases_invoice_tenant_id_invoice_id_credit_card_id");
    }

    [Fact]
    public async Task Compra_na_fatura_do_proprio_cartao_e_aceita()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await NewWalletAsync(db);
        var verde = await NewCardAsync(db, azul.Id);
        var fatura = await SavingAsync(db, NewInvoice(verde.Id));

        var compra = await SavingAsync(db, NewPurchase(verde.Id, fatura.Id));

        (await db.Set<CardPurchase>().CountAsync(p => p.Id == compra.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Nome_do_cartao_e_unico_no_Espaco_sem_diferenciar_caixa_e_acento()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await NewWalletAsync(db);
        var primeiro = await NewCardAsync(db, azul.Id, "Açaí");

        var constraint = await ViolatedConstraintAsync(() => NewCardAsync(db, azul.Id, "  AÇAI "));
        db.ChangeTracker.Clear();

        constraint.ShouldBe("ix_credit_cards_tenant_id_name_normalized");

        // Outro Espaço pode usar o mesmo nome.
        await using var other = CreateContext(setup.ConnectionString, setup.OtherTenantId);
        await NewCardAsync(other, (await NewWalletAsync(other)).Id, "Açaí");

        // Depois da exclusão lógica o nome fica livre.
        db.Remove(await db.Set<CreditCard>().SingleAsync(c => c.Id == primeiro.Id));
        await db.SaveChangesAsync();
        await NewCardAsync(db, azul.Id, "açai");
    }

    [Fact]
    public async Task Credito_parcelado_e_recusado_pelo_CHECK()
    {
        var setup = await SeededAsync();

        var constraint = await ViolatedConstraintAsync(() => ExecuteAsync(
            setup.ConnectionString, "UPDATE card_purchases SET nature = 'credit' WHERE origin = 'installment'"));

        constraint.ShouldBe("ck_card_purchases_credit_not_installment");
    }

    [Fact]
    public async Task Fatura_e_unica_por_cartao_e_mes_de_vencimento()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await NewWalletAsync(db);
        var verde = await NewCardAsync(db, azul.Id);
        var carla = await NewCardAsync(db, azul.Id, "Carla");
        await SavingAsync(db, NewInvoice(verde.Id));
        await SavingAsync(db, NewInvoice(carla.Id));
        await SavingAsync(db, NewInvoice(verde.Id, month: 4));

        var constraint = await ViolatedConstraintAsync(() => SavingAsync(db, NewInvoice(verde.Id)));

        constraint.ShouldBe("ix_invoices_tenant_id_credit_card_id_due_month");
    }

    [Fact]
    public async Task Fks_compostas_recusam_referencias_de_outro_Espaco()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var other = CreateContext(setup.ConnectionString, setup.OtherTenantId);
        var carteiraDeLeo = await NewWalletAsync(other);
        var cartaoDeLeo = await NewCardAsync(other, carteiraDeLeo.Id, "Azul");
        var faturaDeLeo = await SavingAsync(other, NewInvoice(cartaoDeLeo.Id));
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var carteiraDeLia = await NewWalletAsync(db);
        var cartaoDeLia = await NewCardAsync(db, carteiraDeLia.Id);
        var faturaDeLia = await SavingAsync(db, NewInvoice(cartaoDeLia.Id));

        var cartaoComCarteira = await ViolatedConstraintAsync(() => NewCardAsync(db, carteiraDeLeo.Id, "Outro"));
        db.ChangeTracker.Clear();
        var faturaComCartao = await ViolatedConstraintAsync(() => SavingAsync(db, NewInvoice(cartaoDeLeo.Id, month: 4)));
        db.ChangeTracker.Clear();
        var compraComFatura = await ViolatedConstraintAsync(() => SavingAsync(db, NewPurchase(cartaoDeLia.Id, faturaDeLeo.Id)));
        db.ChangeTracker.Clear();
        var pagamentoComFatura = await ViolatedConstraintAsync(
            () => SavingAsync(db, InvoicePayment.Create(faturaDeLeo.Id, carteiraDeLia.Id, Dinheiro.FromCents(100), Hoje)));
        db.ChangeTracker.Clear();
        var pagamentoComCarteira = await ViolatedConstraintAsync(
            () => SavingAsync(db, InvoicePayment.Create(faturaDeLia.Id, carteiraDeLeo.Id, Dinheiro.FromCents(100), Hoje)));

        cartaoComCarteira.ShouldBe("fk_credit_cards_wallet_tenant_id_payment_wallet_id");
        faturaComCartao.ShouldBe("fk_invoices_credit_cards_tenant_id_credit_card_id");
        compraComFatura.ShouldBe("fk_card_purchases_invoice_tenant_id_invoice_id_credit_card_id");
        pagamentoComFatura.ShouldBe("fk_invoice_payments_invoices_tenant_id_invoice_id");
        pagamentoComCarteira.ShouldBe("fk_invoice_payments_wallet_tenant_id_wallet_id");
    }

    [Theory]
    [InlineData("UPDATE credit_cards SET color = 'azul'", "ck_credit_cards_color")]
    [InlineData("UPDATE credit_cards SET closing_day = 0", "ck_credit_cards_closing_day")]
    [InlineData("UPDATE credit_cards SET closing_day = 32", "ck_credit_cards_closing_day")]
    [InlineData("UPDATE credit_cards SET due_day = 0", "ck_credit_cards_due_day")]
    [InlineData("UPDATE credit_cards SET due_day = 32", "ck_credit_cards_due_day")]
    [InlineData("UPDATE credit_cards SET limit_cents = -1", "ck_credit_cards_limit_cents")]
    [InlineData("UPDATE credit_cards SET limit_cents = 1000000000", "ck_credit_cards_limit_cents")]
    [InlineData("UPDATE invoices SET due_month = DATE '2026-03-02'", "ck_invoices_due_month")]
    [InlineData("UPDATE invoices SET closing_date = DATE '1999-12-31'", "ck_invoices_closing_date")]
    [InlineData("UPDATE invoices SET due_date = DATE '2101-01-01'", "ck_invoices_due_date")]
    [InlineData("UPDATE installment_plans SET installment_total = 49, first_number = 1", "ck_installment_plans_installment_total")]
    [InlineData("UPDATE installment_plans SET first_number = 11", "ck_installment_plans_first_number")]
    [InlineData("UPDATE installment_plans SET first_number = 2", "ck_installment_plans_first_number_mode")]
    [InlineData("UPDATE installment_plans SET installment_amount_cents = 1000", "ck_installment_plans_amount_by_mode")]
    [InlineData("UPDATE installment_plans SET total_amount_cents = NULL", "ck_installment_plans_amount_by_mode")]
    [InlineData("UPDATE installment_plans SET total_amount_cents = 0", "ck_installment_plans_total_amount_cents")]
    [InlineData("UPDATE installment_plans SET purchase_date = DATE '1999-12-31'", "ck_installment_plans_purchase_date")]
    [InlineData("UPDATE installment_plans SET first_invoice_due_month = DATE '2026-03-02'", "ck_installment_plans_first_invoice_due_month")]
    [InlineData("UPDATE card_purchases SET nature = 'refund'", "ck_card_purchases_nature")]
    [InlineData("UPDATE card_purchases SET origin = 'recurrence'", "ck_card_purchases_origin")]
    [InlineData("UPDATE card_purchases SET amount_cents = 0", "ck_card_purchases_amount_cents")]
    [InlineData("UPDATE card_purchases SET amount_cents = 1000000000", "ck_card_purchases_amount_cents")]
    [InlineData("UPDATE card_purchases SET purchase_date = DATE '2101-01-01'", "ck_card_purchases_purchase_date")]
    [InlineData("UPDATE card_purchases SET installment_number = NULL WHERE origin = 'installment'", "ck_card_purchases_installment_fields")]
    [InlineData("UPDATE card_purchases SET installment_plan_id = NULL, installment_number = NULL, installment_total = NULL WHERE origin = 'installment'", "ck_card_purchases_installment_fields")]
    [InlineData("UPDATE card_purchases SET origin = 'installment' WHERE origin = 'manual'", "ck_card_purchases_installment_fields")]
    [InlineData("UPDATE card_purchases SET installment_number = 11 WHERE origin = 'installment'", "ck_card_purchases_installment_number")]
    [InlineData("UPDATE card_purchases SET origin = 'carry_over' WHERE origin = 'manual'", "ck_card_purchases_carry_over_pair")]
    [InlineData("UPDATE card_purchases SET carry_over_pair_id = gen_random_uuid() WHERE origin = 'manual'", "ck_card_purchases_carry_over_pair")]
    [InlineData("UPDATE invoice_payments SET amount_cents = 0", "ck_invoice_payments_amount_cents")]
    [InlineData("UPDATE invoice_payments SET payment_date = DATE '1999-12-31'", "ck_invoice_payments_payment_date")]
    public async Task Banco_recusa_valores_fora_das_regras(string update, string expectedConstraint)
    {
        var setup = await SeededAsync();

        var constraint = await ViolatedConstraintAsync(() => ExecuteAsync(setup.ConnectionString, update));

        constraint.ShouldBe(expectedConstraint);
    }

    [Fact]
    public async Task Tem_os_indices_do_modelo_de_dados()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' "
            + "AND tablename IN ('credit_cards', 'invoices', 'installment_plans', 'card_purchases', 'invoice_payments')")
            .ToListAsync();

        indexes.ShouldContain("ix_credit_cards_tenant_id_name_normalized");
        indexes.ShouldContain("ix_invoices_tenant_id_credit_card_id_due_month");
        indexes.ShouldContain("ix_invoices_tenant_id_due_month");
        indexes.ShouldContain("ak_invoices_tenant_id_id_credit_card_id");
        indexes.ShouldContain("ix_card_purchases_tenant_id_invoice_id");
        indexes.ShouldContain("ix_card_purchases_tenant_id_installment_plan_id");
        indexes.ShouldContain("ix_card_purchases_tenant_id_description_normalized");
        indexes.ShouldContain("ix_invoice_payments_tenant_id_invoice_id");
    }

    [Fact]
    public async Task GetOrCreate_cria_a_fatura_com_as_datas_do_ciclo_e_depois_devolve_a_mesma()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await NewWalletAsync(db);
        var verde = await NewCardAsync(db, azul.Id, closing: 5, due: 15);
        var repository = new InvoiceRepository(db, PostgresFixture.DefaultClock);

        var criada = await repository.GetOrCreateAsync(verde.Id, YearMonth.Of(2026, 3), CancellationToken.None);
        var outraVez = await repository.GetOrCreateAsync(verde.Id, YearMonth.Of(2026, 3), CancellationToken.None);

        outraVez.Id.ShouldBe(criada.Id);
        criada.CreditCardId.ShouldBe(verde.Id);
        criada.DueMonth.ShouldBe(new DateOnly(2026, 3, 1));
        criada.ClosingDate.ShouldBe(new DateOnly(2026, 3, 5));
        criada.DueDate.ShouldBe(new DateOnly(2026, 3, 15));
        criada.TenantId.ShouldBe(setup.TenantId);
        criada.CreatedAt.ShouldBe(PostgresFixture.DefaultClock.UtcNow);
        criada.CreatedVia.ShouldBe(Channel.App);
        (await db.Set<Invoice>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task GetOrCreate_com_fechamento_maior_ou_igual_ao_vencimento_fecha_no_mes_anterior()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var azul = await NewWalletAsync(db);
        var verde = await NewCardAsync(db, azul.Id, closing: 30, due: 10);
        var repository = new InvoiceRepository(db, PostgresFixture.DefaultClock);

        var fatura = await repository.GetOrCreateAsync(verde.Id, YearMonth.Of(2026, 3), CancellationToken.None);

        fatura.ClosingDate.ShouldBe(new DateOnly(2026, 2, 28));
        fatura.DueDate.ShouldBe(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public async Task GetOrCreate_simultaneo_termina_na_mesma_linha_sem_erro()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        Guid cardId;
        await using (var db = CreateContext(setup.ConnectionString, setup.TenantId))
        {
            cardId = (await NewCardAsync(db, (await NewWalletAsync(db)).Id)).Id;
        }

        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
            var repository = new InvoiceRepository(db, PostgresFixture.DefaultClock);
            return (await repository.GetOrCreateAsync(cardId, YearMonth.Of(2026, 3), CancellationToken.None)).Id;
        }));

        ids.Distinct().Count().ShouldBe(1);
        await using var read = CreateContext(setup.ConnectionString, setup.TenantId);
        (await read.Set<Invoice>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task GetOrCreate_de_cartao_de_outro_Espaco_falha_e_nao_cria_fatura()
    {
        var setup = await MigratedWithTwoTenantsAsync();
        await using var other = CreateContext(setup.ConnectionString, setup.OtherTenantId);
        var cartaoDeLeo = await NewCardAsync(other, (await NewWalletAsync(other)).Id, "Azul");
        await using var db = CreateContext(setup.ConnectionString, setup.TenantId);
        var repository = new InvoiceRepository(db, PostgresFixture.DefaultClock);

        await Should.ThrowAsync<InvalidOperationException>(
            () => repository.GetOrCreateAsync(cartaoDeLeo.Id, YearMonth.Of(2026, 3), CancellationToken.None));

        await using var read = CreateContext(setup.ConnectionString, setup.OtherTenantId);
        (await read.Set<Invoice>().CountAsync()).ShouldBe(0);
    }
}
