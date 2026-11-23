using System.Net;
using System.Net.Http.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Cards;
using FinMy.Domain.Monetary;
using Microsoft.EntityFrameworkCore;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Cards;

/// <summary>
/// Mudança de dias de fechamento/vencimento do cartão (003 Edge Case, research R3): faturas futuras recalculadas,
/// compras redistribuídas pela regra (FR-008/FR-012), exceto movidas manualmente, parcelamento em andamento e
/// faturas fechadas; nenhuma compra vai para fatura já fechada.
/// </summary>
public class CardScheduleChangeTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    // Hoje = 20/03/2026. Cartão Azul: fecha 18, vence 25. A fatura 2026-03 já fechou (18/03); 2026-04 e seguintes estão abertas.
    // O token é emitido com o relógio real (o JWT é validado pelo relógio do sistema); o relógio de teste vai
    // para a data fixa depois do cadastro da conta.
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private sealed record Seeded(
        CreditCardDto Card,
        TestAccount Account,
        HttpClient Http,
        CardPurchase Closed,
        CardPurchase Later,
        CardPurchase Moved,
        CardPurchase Stays,
        CardPurchase[] Installments,
        CardPurchase[] InProgress,
        CardPurchase Credit);

    private static YearMonth M(int year, int month) => YearMonth.Of(year, month);

    private async Task<Seeded> SeedAsync()
    {
        var (http, account) = await CardTestSupport.NewSpaceAsync(Factory);
        _clock.UtcNow = new DateTimeOffset(2026, 3, 20, 15, 0, 0, TimeSpan.Zero);
        var wallet = await CreateWalletAsync(http, "Azul");
        var card = await CreateCardAsync(http, "Azul", wallet.Id);

        Invoice InvoiceOf(YearMonth month) =>
            Invoice.Create(card.Id, month, InvoiceCycle.ClosingDate(month, 18, 25), InvoiceCycle.DueDate(month, 25));

        var march = InvoiceOf(M(2026, 3));
        var april = InvoiceOf(M(2026, 4));
        var may = InvoiceOf(M(2026, 5));
        var june = InvoiceOf(M(2026, 6));

        CardPurchase Single(Invoice invoice, string description, long cents, DateOnly date, PurchaseNature nature = PurchaseNature.Debit) =>
            CardPurchase.CreateManual(card.Id, invoice.Id, description, Money.FromCents(cents), nature, date, null);

        // Fatura de março (fechada em 18/03): não pode ser tocada pela mudança de dias.
        var closed = Single(march, "Mercado", 10000, new DateOnly(2026, 3, 10));

        // Fatura de abril (aberta).
        var later = Single(april, "Cinema", 5000, new DateOnly(2026, 4, 12));      // 12/04: com fechamento dia 10 passa para maio.
        var stays = Single(april, "Uber", 1295, new DateOnly(2026, 3, 22));        // 22/03: continua em abril nos dois cenários.
        var moved = Single(april, "Farmácia", 3000, new DateOnly(2026, 4, 12));    // igual a "Cinema", mas movida à mão.
        moved.MoveToInvoice(april.Id);
        var credit = Single(april, "Estorno", 700, new DateOnly(2026, 4, 12), PurchaseNature.Credit);

        // Casa China 3x de R$ 100,00 em 12/04: parcelas 1/3 em abril, 2/3 em maio, 3/3 em junho.
        var plan = InstallmentPlan.Create(card.Id, "Casa China", InstallmentMode.TotalAmount, 3, 1, Money.FromCents(10000), new DateOnly(2026, 4, 12), M(2026, 4));
        var installments = new[]
        {
            CardPurchase.CreateInstallment(card.Id, april.Id, plan, 1, Money.FromCents(3334), null),
            CardPurchase.CreateInstallment(card.Id, may.Id, plan, 2, Money.FromCents(3333), null),
            CardPurchase.CreateInstallment(card.Id, june.Id, plan, 3, Money.FromCents(3333), null),
        };

        // Parcelamento em andamento: parcelas 3/5 a 5/5 a partir de abril, escolhidas pela pessoa.
        var running = InstallmentPlan.Create(card.Id, "Shoppe Casa", InstallmentMode.InProgress, 5, 3, Money.FromCents(10274), new DateOnly(2026, 1, 20), M(2026, 4));
        var inProgress = new[]
        {
            CardPurchase.CreateInstallment(card.Id, april.Id, running, 3, Money.FromCents(10274), null),
            CardPurchase.CreateInstallment(card.Id, may.Id, running, 4, Money.FromCents(10274), null),
            CardPurchase.CreateInstallment(card.Id, june.Id, running, 5, Money.FromCents(10274), null),
        };

        await Months.MonthSeeding.SeedAsync(
            Factory, account, march, april, may, june, closed, later, stays, moved, credit, plan, installments[0], installments[1], installments[2],
            running, inProgress[0], inProgress[1], inProgress[2]);

        return new Seeded(card, account, http, closed, later, moved, stays, installments, inProgress, credit);
    }

    private static object Body(CreditCardDto card, int closingDay, int dueDay) =>
        new { name = card.Name, closingDay, dueDay, paymentWalletId = card.PaymentWalletId, color = card.Color, limit = (string?)null, version = card.Version };

    /// <summary>Mês de vencimento da fatura onde cada compra está agora, lido direto do banco.</summary>
    private async Task<Dictionary<Guid, string>> InvoiceMonthsAsync(Seeded seed) =>
        await WithDbAsync(Factory, seed.Account, async (db, _) =>
        {
            var rows = await db.Set<CardPurchase>().Join(
                    db.Set<Invoice>(), p => p.InvoiceId, i => i.Id, (p, i) => new { p.Id, i.DueMonth })
                .ToListAsync();
            return rows.ToDictionary(r => r.Id, r => YearMonth.From(r.DueMonth).ToString());
        });

    private async Task<Dictionary<string, (DateOnly Closing, DateOnly Due)>> InvoiceDatesAsync(Seeded seed) =>
        await WithDbAsync(Factory, seed.Account, async (db, _) =>
        {
            var rows = await db.Set<Invoice>().Where(i => i.CreditCardId == seed.Card.Id).ToListAsync();
            return rows.ToDictionary(i => i.DueYearMonth.ToString(), i => (i.ClosingDate, i.DueDate));
        });

    [Fact]
    public async Task Fechamento_mais_cedo_recalcula_as_faturas_futuras_e_redistribui_as_compras_automaticas()
    {
        var seed = await SeedAsync();

        var response = await seed.Http.PutAsJsonAsync($"{CardsUrl}/{seed.Card.Id}", Body(seed.Card, closingDay: 10, dueDay: 20));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var months = await InvoiceMonthsAsync(seed);

        // À vista: 12/04 com fechamento dia 10 deixa de caber em abril (fecha 10/04) e vai para maio; 22/03 continua em abril.
        months[seed.Later.Id].ShouldBe("2026-05");
        months[seed.Credit.Id].ShouldBe("2026-05");
        months[seed.Stays.Id].ShouldBe("2026-04");

        // Movida à mão, fatura fechada e parcelamento em andamento não mudam.
        months[seed.Moved.Id].ShouldBe("2026-04");
        months[seed.Closed.Id].ShouldBe("2026-03");
        months[seed.InProgress[0].Id].ShouldBe("2026-04");
        months[seed.InProgress[1].Id].ShouldBe("2026-05");
        months[seed.InProgress[2].Id].ShouldBe("2026-06");

        // Parcelado em 12/04: 1ª parcela em maio e as seguintes nos meses seguintes (FR-012).
        months[seed.Installments[0].Id].ShouldBe("2026-05");
        months[seed.Installments[1].Id].ShouldBe("2026-06");
        months[seed.Installments[2].Id].ShouldBe("2026-07");
    }

    [Fact]
    public async Task Faturas_futuras_ganham_as_datas_novas_e_as_fechadas_mantem_as_antigas_e_as_novas_linhas_nascem_com_os_dias_novos()
    {
        var seed = await SeedAsync();

        (await seed.Http.PutAsJsonAsync($"{CardsUrl}/{seed.Card.Id}", Body(seed.Card, closingDay: 10, dueDay: 20))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var dates = await InvoiceDatesAsync(seed);

        dates["2026-03"].ShouldBe((new DateOnly(2026, 3, 18), new DateOnly(2026, 3, 25)));
        dates["2026-04"].ShouldBe((new DateOnly(2026, 4, 10), new DateOnly(2026, 4, 20)));
        dates["2026-05"].ShouldBe((new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 20)));
        dates["2026-06"].ShouldBe((new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 20)));
        dates["2026-07"].ShouldBe((new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 20)));

        var overview = await GetCardAsync(seed.Http, seed.Card.Id);
        overview.ClosingDay.ShouldBe(10);
        overview.DueDay.ShouldBe(20);
        overview.CurrentInvoice!.DueMonth.ShouldBe("2026-04");
        overview.CurrentInvoice.ClosingDate.ShouldBe(new DateOnly(2026, 4, 10));
        overview.CurrentInvoice.DueDate.ShouldBe(new DateOnly(2026, 4, 20));
        // Abril perdeu Cinema (50,00), Estorno e a parcela 1/3; ficou com Uber, Farmácia e Shoppe 3/5.
        overview.CurrentInvoice.TotalCents.ShouldBe(1295 + 3000 + 10274);
        overview.OpenPastInvoices.ShouldHaveSingleItem().ClosingDate.ShouldBe(new DateOnly(2026, 3, 18));
    }

    [Fact]
    public async Task Fechamento_mais_tarde_nao_leva_compra_para_fatura_ja_fechada()
    {
        var seed = await SeedAsync();

        // Fecha dia 25 (vence 28): 22/03 passaria a caber na fatura de março, mas ela fechou em 18/03.
        var response = await seed.Http.PutAsJsonAsync($"{CardsUrl}/{seed.Card.Id}", Body(seed.Card, closingDay: 25, dueDay: 28));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var months = await InvoiceMonthsAsync(seed);
        months[seed.Stays.Id].ShouldBe("2026-04");
        months[seed.Closed.Id].ShouldBe("2026-03");

        var dates = await InvoiceDatesAsync(seed);
        dates["2026-03"].ShouldBe((new DateOnly(2026, 3, 18), new DateOnly(2026, 3, 25)));
        dates["2026-04"].ShouldBe((new DateOnly(2026, 4, 25), new DateOnly(2026, 4, 28)));
    }

    [Fact]
    public async Task Fechamento_depois_do_vencimento_usa_o_mes_anterior_e_so_recalcula_faturas_cujo_novo_fechamento_e_posterior_a_hoje()
    {
        var seed = await SeedAsync();

        // Fecha 5, vence 2: o fechamento é no mês anterior ao vencimento. A fatura de abril passaria a fechar em
        // 05/03, antes de hoje (20/03), então não é recalculada (nem suas compras); a de maio (05/04) e a de junho (05/05) são.
        var response = await seed.Http.PutAsJsonAsync($"{CardsUrl}/{seed.Card.Id}", Body(seed.Card, closingDay: 5, dueDay: 2));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dates = await InvoiceDatesAsync(seed);
        dates["2026-04"].ShouldBe((new DateOnly(2026, 4, 18), new DateOnly(2026, 4, 25)));
        dates["2026-05"].ShouldBe((new DateOnly(2026, 4, 5), new DateOnly(2026, 5, 2)));
        dates["2026-06"].ShouldBe((new DateOnly(2026, 5, 5), new DateOnly(2026, 6, 2)));
        var months = await InvoiceMonthsAsync(seed);
        months[seed.Later.Id].ShouldBe("2026-04");
        months[seed.Installments[0].Id].ShouldBe("2026-04");
        months[seed.Installments[1].Id].ShouldBe("2026-07"); // 12/04 cai na fatura de junho (fecha 05/05); a parcela 2 vem um mês depois.
    }

    [Fact]
    public async Task Editar_sem_mudar_os_dias_nao_mexe_nas_faturas_nem_nas_compras()
    {
        var seed = await SeedAsync();
        var antesMeses = await InvoiceMonthsAsync(seed);
        var antesDatas = await InvoiceDatesAsync(seed);

        var response = await seed.Http.PutAsJsonAsync(
            $"{CardsUrl}/{seed.Card.Id}",
            new { name = "Azul Gold", closingDay = 18, dueDay = 25, paymentWalletId = seed.Card.PaymentWalletId, color = "#43A047", limit = "2.000,00", version = seed.Card.Version });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await InvoiceMonthsAsync(seed)).ShouldBe(antesMeses, ignoreOrder: true);
        (await InvoiceDatesAsync(seed)).ShouldBe(antesDatas, ignoreOrder: true);
    }

    [Fact]
    public async Task Mudar_so_o_vencimento_recalcula_as_datas_e_mantem_as_compras_no_lugar()
    {
        var seed = await SeedAsync();
        var antes = await InvoiceMonthsAsync(seed);

        // Fecha 18, vence 28: a mesma fatura de cada compra; só o vencimento muda.
        (await seed.Http.PutAsJsonAsync($"{CardsUrl}/{seed.Card.Id}", Body(seed.Card, closingDay: 18, dueDay: 28))).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await InvoiceMonthsAsync(seed)).ShouldBe(antes, ignoreOrder: true);
        var dates = await InvoiceDatesAsync(seed);
        dates["2026-03"].Due.ShouldBe(new DateOnly(2026, 3, 25));
        dates["2026-04"].ShouldBe((new DateOnly(2026, 4, 18), new DateOnly(2026, 4, 28)));
    }

    [Fact]
    public async Task Dia_31_em_mes_curto_usa_o_ultimo_dia_do_mes_nas_faturas_recalculadas()
    {
        var seed = await SeedAsync();

        // Fecha 31, vence 5: o fechamento é no mês anterior ao vencimento (RT-03): fatura de abril fecha em 31/03, a de junho em 31/05.
        (await seed.Http.PutAsJsonAsync($"{CardsUrl}/{seed.Card.Id}", Body(seed.Card, closingDay: 31, dueDay: 5))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var dates = await InvoiceDatesAsync(seed);
        dates["2026-04"].ShouldBe((new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 5)));
        dates["2026-05"].ShouldBe((new DateOnly(2026, 4, 30), new DateOnly(2026, 5, 5)));
        dates["2026-06"].ShouldBe((new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 5)));
        var months = await InvoiceMonthsAsync(seed);
        months[seed.Stays.Id].ShouldBe("2026-04");
    }
}
