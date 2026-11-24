using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Wallets;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Panel;

/// <summary><c>GET /api/v1/years/{yyyy}/panel</c> (006 T001, SC-003): 3.000 lançamentos no ano, p95 local abaixo de 2 s.</summary>
public class PanelPerformanceTests(PostgresFixture postgres, ITestOutputHelper output) : ApiTestBase(postgres)
{
    private const int Entries = 3000;
    private const int Requests = 20;

    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    [Fact]
    public async Task SC_003_painel_com_3000_lancamentos_no_ano_responde_com_p95_abaixo_de_2_segundos()
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, account) = await NewSpaceAsync(Factory);
        var azul = await CreateWalletAsync(http, "Azul");
        var verde = await CreateWalletAsync(http, "Verde");
        var card = await CreateCardAsync(http, "Azul", azul.Id);
        _clock.UtcNow = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        // Em massa, direto pelo DbContext (um SaveChanges só): 250 lançamentos por mês, entre Entradas e Saídas, Previstos e Realizados.
        await WithDbAsync(Factory, account, async (db, _) =>
        {
            for (var i = 0; i < Entries; i++)
            {
                var month = i % 12 + 1;
                var date = new DateOnly(2026, month, 1 + i % 27);
                var kind = i % 3 == 0 ? EntryKind.Income : EntryKind.Expense;
                var status = i % 2 == 0 ? EntryStatus.Settled : EntryStatus.Planned;
                db.Add(Entry.Create(kind, $"Lançamento {i}", Money.FromCents(1000 + i), date, i % 2 == 0 ? azul.Id : verde.Id, status, null, null, date));
            }

            await db.SaveChangesAsync();
            return 0;
        });

        // Faturas de alguns meses, para o painel também agregar compras e pagamentos.
        for (var month = 1; month <= 6; month++)
        {
            var response = await http.PostAsJsonAsync(
                "/api/v1/card-purchases",
                new { description = "Mercado", amount = "150", date = $"2026-{month:00}-10", creditCardId = card.Id });
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }

        // Aquecimento: a primeira chamada paga o custo de JIT e do plano das consultas.
        (await http.GetAsync("/api/v1/years/2026/panel")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var elapsed = new List<double>();
        for (var i = 0; i < Requests; i++)
        {
            var watch = Stopwatch.StartNew();
            var response = await http.GetAsync("/api/v1/years/2026/panel");
            watch.Stop();
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            elapsed.Add(watch.Elapsed.TotalMilliseconds);
        }

        elapsed.Sort();
        var p95 = elapsed[(int)Math.Ceiling(Requests * 0.95) - 1];
        output.WriteLine($"Painel do ano com {Entries} lançamentos: p50={elapsed[Requests / 2]:F0} ms, p95={p95:F0} ms, máx={elapsed[^1]:F0} ms ({Requests} chamadas)");
        p95.ShouldBeLessThan(2000);
    }
}
