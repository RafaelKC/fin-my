using FinMy.Application.Abstractions;
using FinMy.Application.Recurrences;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Recurrences;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Jobs;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Application.Tests.Jobs;

/// <summary>
/// Job diário do horizonte (005 research R2): US1.2 com relógio fixo em 2027-01-01, idempotência, ocorrência excluída que
/// não volta (US3.3), Espaços independentes e carteira arquivada. Dados fictícios (carteira Azul, cartão Verde).
/// </summary>
[Collection(PostgresTestGroup.Name)]
public class RecurrenceHorizonJobTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Marco2026 = new(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Janeiro2027 = new(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(Marco2026);

    private readonly List<Guid> _tenants = [];

    private sealed class TenantList(List<Guid> ids) : ITenantIdSource
    {
        public Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddFinMyTenantJobRunner();
        services.AddFinMyRecurrenceHorizon();
        services.AddSingleton<ITenantIdSource>(new TenantList(_tenants));
        services.AddScoped<CurrentContext>();
        services.AddScoped<ICurrentContext>(sp => sp.GetRequiredService<CurrentContext>());
        services.AddSingleton<IClock>(_clock);
        services.AddDbContext<TestFinMyDbContext>((sp, options) =>
            options.UseFinMyNpgsql(postgres.ConnectionString).AddFinMyInterceptors(sp.GetRequiredService<IClock>()));
        services.AddScoped<FinMyDbContext>(sp => sp.GetRequiredService<TestFinMyDbContext>());
        services.AddScoped<IRecurrenceStore, RecurrenceStore>();
        services.AddScoped<IEntryStore, EntryStore>();
        services.AddScoped<ICreditCardStore, CreditCardStore>();
        services.AddScoped<ICardPurchaseStore, CardPurchaseStore>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<ITagStore, TagStore>();
        services.AddScoped<RecurrenceReviewer>();
        services.AddScoped<RecurrenceGenerator>();

        return services.BuildServiceProvider();
    }

    private async Task<Guid> NewTenantAsync()
    {
        var tenantId = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenantId);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO tenants (id, name, onboarding_steps, created_at, created_by, created_via, updated_at, updated_by, updated_via)
            VALUES ({tenantId}, 'Espaço de teste', jsonb_build_object(), now(), {tenantId}, 'system', now(), {tenantId}, 'system')
            """);
        _tenants.Add(tenantId);
        return tenantId;
    }

    private static Wallet NewWallet(string name = "Azul") => Wallet.Create(name, WalletType.Checking, "#1E88E5", Money.Zero, new DateOnly(2026, 1, 1));

    private static Recurrence Tim(Guid walletId, YearMonth start) =>
        Recurrence.Create(
            RecurrenceKind.Expense, "Tim", Money.FromCents(4499), 10, walletId, null, null, start,
            RecurrenceEndType.None, null, null, ReferenceMonthRule.SameMonth);

    private async Task<(Guid Tenant, Guid Wallet, Guid Recurrence)> SeedTimAsync()
    {
        var tenant = await NewTenantAsync();
        var wallet = NewWallet();
        var recurrence = Tim(wallet.Id, YearMonth.Of(2026, 3));
        await using var db = postgres.CreateContext(tenant);
        db.AddRange(wallet, recurrence);
        await db.SaveChangesAsync();
        return (tenant, wallet.Id, recurrence.Id);
    }

    private async Task<List<Entry>> EntriesAsync(Guid tenant, Guid recurrenceId)
    {
        await using var db = postgres.CreateContext(tenant);
        return await db.Set<Entry>().AsNoTracking().Where(e => e.RecurrenceId == recurrenceId).OrderBy(e => e.RecurrenceMonth).ToListAsync();
    }

    private async Task<YearMonth?> GeneratedUntilAsync(Guid tenant, Guid recurrenceId)
    {
        await using var db = postgres.CreateContext(tenant);
        return (await db.Set<Recurrence>().AsNoTracking().SingleAsync(r => r.Id == recurrenceId)).GeneratedUntil;
    }

    private async Task RunAsync()
    {
        await using var provider = BuildServices();
        await provider.GetRequiredService<RecurrenceHorizonJob>().RunAsync();
    }

    [Fact]
    public async Task Gera_as_ocorrencias_ate_dezembro_do_ano_seguinte_como_sistema_e_e_idempotente()
    {
        var (tenant, wallet, recurrence) = await SeedTimAsync();

        await RunAsync();
        await RunAsync();

        var entries = await EntriesAsync(tenant, recurrence);
        entries.Count.ShouldBe(22);
        entries[0].EntryDate.ShouldBe(new DateOnly(2026, 3, 10));
        entries[^1].EntryDate.ShouldBe(new DateOnly(2027, 12, 10));
        entries.ShouldAllBe(e => e.Status == EntryStatus.Planned && e.WalletId == wallet && e.CreatedVia == Channel.System);
        (await GeneratedUntilAsync(tenant, recurrence)).ShouldBe(YearMonth.Of(2027, 12));
    }

    [Fact]
    public async Task Virada_do_ano_estende_o_horizonte_ate_dezembro_de_2028()
    {
        var (tenant, _, recurrence) = await SeedTimAsync();
        await RunAsync();

        _clock.UtcNow = Janeiro2027;
        await RunAsync();
        await RunAsync();

        var entries = await EntriesAsync(tenant, recurrence);
        entries.Count.ShouldBe(34);
        entries.Select(e => e.RecurrenceMonth).Distinct().Count().ShouldBe(34);
        entries[22].EntryDate.ShouldBe(new DateOnly(2028, 1, 10));
        entries[^1].EntryDate.ShouldBe(new DateOnly(2028, 12, 10));
        (await GeneratedUntilAsync(tenant, recurrence)).ShouldBe(YearMonth.Of(2028, 12));
    }

    [Fact]
    public async Task Ocorrencia_excluida_nao_volta_quando_o_horizonte_e_estendido()
    {
        var (tenant, _, recurrence) = await SeedTimAsync();
        await RunAsync();

        await using (var db = postgres.CreateContext(tenant))
        {
            var dezembro = await db.Set<Entry>().SingleAsync(e => e.RecurrenceId == recurrence && e.RecurrenceMonth == new DateOnly(2026, 12, 1));
            db.Remove(dezembro);
            await db.SaveChangesAsync();
        }

        _clock.UtcNow = Janeiro2027;
        await RunAsync();

        var entries = await EntriesAsync(tenant, recurrence);
        entries.Count.ShouldBe(33);
        entries.ShouldNotContain(e => e.RecurrenceMonth == new DateOnly(2026, 12, 1));
        entries.Select(e => e.RecurrenceMonth).Distinct().Count().ShouldBe(33);
    }

    [Fact]
    public async Task Recorrencia_encerrada_no_fim_nao_gera_alem_do_mes_final()
    {
        var tenant = await NewTenantAsync();
        var wallet = NewWallet();
        var recurrence = Recurrence.Create(
            RecurrenceKind.Expense, "Curso", Money.FromCents(12_000), 8, wallet.Id, null, null, YearMonth.Of(2026, 3),
            RecurrenceEndType.AfterCount, null, 6, ReferenceMonthRule.SameMonth);
        await using (var db = postgres.CreateContext(tenant))
        {
            db.AddRange(wallet, recurrence);
            await db.SaveChangesAsync();
        }

        await RunAsync();
        _clock.UtcNow = Janeiro2027;
        await RunAsync();

        (await EntriesAsync(tenant, recurrence.Id)).Count.ShouldBe(6);
        (await GeneratedUntilAsync(tenant, recurrence.Id)).ShouldBe(YearMonth.Of(2026, 8));
    }

    [Fact]
    public async Task Processa_cada_Espaco_e_so_os_listados()
    {
        var (tenantA, _, recurrenceA) = await SeedTimAsync();
        var (tenantB, _, recurrenceB) = await SeedTimAsync();
        _tenants.Remove(tenantB);

        await RunAsync();

        (await EntriesAsync(tenantA, recurrenceA)).Count.ShouldBe(22);
        (await EntriesAsync(tenantB, recurrenceB)).ShouldBeEmpty("o Espaço B não estava na lista do job.");

        _tenants.Add(tenantB);
        await RunAsync();

        (await EntriesAsync(tenantB, recurrenceB)).Count.ShouldBe(22);
        (await EntriesAsync(tenantA, recurrenceA)).Count.ShouldBe(22);
    }

    [Fact]
    public async Task Carteira_arquivada_nao_gera_nada_e_ao_desarquivar_a_proxima_execucao_cria_os_meses_pendentes()
    {
        var tenant = await NewTenantAsync();
        var wallet = NewWallet();
        var recurrence = Tim(wallet.Id, YearMonth.Of(2026, 3));
        wallet.Archive(Marco2026);
        await using (var db = postgres.CreateContext(tenant))
        {
            db.AddRange(wallet, recurrence);
            await db.SaveChangesAsync();
        }

        await RunAsync();

        (await EntriesAsync(tenant, recurrence.Id)).ShouldBeEmpty();
        (await GeneratedUntilAsync(tenant, recurrence.Id)).ShouldBeNull();

        await using (var db = postgres.CreateContext(tenant))
        {
            (await db.Set<Wallet>().SingleAsync(w => w.Id == wallet.Id)).Unarchive();
            await db.SaveChangesAsync();
        }

        _clock.UtcNow = Janeiro2027;
        await RunAsync();

        // Início em 2026-03, ainda no horizonte de 2027: os meses pendentes saem de 2026-03 a 2028-12.
        var entries = await EntriesAsync(tenant, recurrence.Id);
        entries.Count.ShouldBe(34);
        entries[0].EntryDate.ShouldBe(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public async Task Cartao_arquivado_nao_gera_compras_e_cartao_ativo_gera_na_fatura_do_ciclo()
    {
        var tenant = await NewTenantAsync();
        var wallet = NewWallet();
        var ativo = CreditCard.Create("Verde", "#43A047", 18, 25, wallet.Id);
        var arquivado = CreditCard.Create("Cinza", "#9E9E9E", 18, 25, wallet.Id);
        arquivado.Archive(Marco2026);
        var primeira = Recurrence.Create(
            RecurrenceKind.CardPurchase, "Prime Video", Money.FromCents(1990), 2, null, ativo.Id, null, YearMonth.Of(2026, 7),
            RecurrenceEndType.None, null, null, null);
        var segunda = Recurrence.Create(
            RecurrenceKind.CardPurchase, "WellHub", Money.FromCents(9990), 3, null, arquivado.Id, null, YearMonth.Of(2026, 7),
            RecurrenceEndType.None, null, null, null);
        await using (var db = postgres.CreateContext(tenant))
        {
            db.AddRange(wallet, ativo, arquivado, primeira, segunda);
            await db.SaveChangesAsync();
        }

        await RunAsync();

        await using var read = postgres.CreateContext(tenant);
        var purchases = await read.Set<CardPurchase>().AsNoTracking().OrderBy(p => p.RecurrenceMonth).ToListAsync();
        purchases.Count.ShouldBe(18);
        purchases.ShouldAllBe(p => p.RecurrenceId == primeira.Id && p.Origin == PurchaseOrigin.Recurrence && p.Nature == PurchaseNature.Debit);
        purchases[0].PurchaseDate.ShouldBe(new DateOnly(2026, 7, 2));
        var invoice = await read.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == purchases[0].InvoiceId);
        invoice.DueMonth.ShouldBe(new DateOnly(2026, 7, 1));
        (await read.Set<Recurrence>().AsNoTracking().SingleAsync(r => r.Id == segunda.Id)).GeneratedUntil.ShouldBeNull();
    }

    [Fact]
    public void Descreve_a_execucao_diaria_as_3h_de_Brasilia()
    {
        string? id = null;
        string? cron = null;
        TimeZoneInfo? zone = null;

        RecurrenceHorizonJob.Describe((i, c, z) => (id, cron, zone) = (i, c, z));

        id.ShouldBe("recurrence-horizon");
        cron.ShouldBe("0 3 * * *");
        zone!.Id.ShouldBe("America/Sao_Paulo");
    }
}
