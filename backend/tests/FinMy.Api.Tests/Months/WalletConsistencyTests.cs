using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Wallets;
using static FinMy.Api.Tests.Months.MonthSeeding;

namespace FinMy.Api.Tests.Months;

/// <summary>
/// SC-003 (research R5): a soma dos saldos atuais das Carteiras é igual ao Saldo final Realizado (todas as Carteiras)
/// do último mês que contém um lançamento efetivado ou uma data de saldo inicial. Os saldos atuais vêm de
/// <c>GET /wallets</c> (<c>currentBalanceCents</c>) e também da fórmula da R5 aplicada aos próprios dados gerados,
/// que não depende de nenhuma das duas consultas.
/// </summary>
public class WalletConsistencyTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const int EntryCount = 200;

    private sealed record Generated(List<Wallet> Wallets, List<Entry> Entries, List<FinMy.Domain.Transfers.Transfer> Transfers);

    private static Generated Generate(int seed)
    {
        var random = new Random(seed);
        var initialDates = new[] { new DateOnly(2024, 1, 1), new DateOnly(2024, 9, 15), new DateOnly(2025, 6, 30) };
        var wallets = new List<Wallet>
        {
            NewWallet("Azul", 120000, initialDates[0], primary: true),
            NewWallet("Verde", -35000, initialDates[1]),
            NewWallet("Poupança da Bruna", 0, initialDates[2]),
        };

        var entries = new List<Entry>();
        for (var i = 0; i < EntryCount; i++)
        {
            var date = new DateOnly(2024, 1, 1).AddDays(random.Next(0, 3 * 365));
            var kind = random.Next(100) < 35 ? EntryKind.Income : EntryKind.Expense;
            var status = random.Next(100) < 70 ? EntryStatus.Settled : EntryStatus.Planned;
            var reference = YearMonth.From(date);
            if (random.Next(100) < 15)
            {
                reference = reference.AddMonths(random.Next(-2, 3));
            }

            entries.Add(NewEntry(kind, $"Lançamento {i}", random.Next(1, 90000), date, wallets[random.Next(wallets.Count)].Id, status, reference));
        }

        var transfers = new List<FinMy.Domain.Transfers.Transfer>();
        for (var i = 0; i < 30; i++)
        {
            var from = random.Next(wallets.Count);
            var to = (from + 1 + random.Next(wallets.Count - 1)) % wallets.Count;
            var date = new DateOnly(2024, 1, 1).AddDays(random.Next(0, 3 * 365));
            transfers.Add(NewTransfer(wallets[from].Id, wallets[to].Id, random.Next(1, 40000), date));
        }

        return new Generated(wallets, entries, transfers);
    }

    /// <summary>Saldo atual de uma carteira (R5): inicial + recebidas − pagas + transferências recebidas − enviadas.</summary>
    private static long CurrentBalance(Generated data, Wallet wallet) =>
        wallet.InitialBalanceCents
        + data.Entries.Where(e => e.WalletId == wallet.Id && e.Status == EntryStatus.Settled)
            .Sum(e => e.Kind == EntryKind.Income ? e.AmountCents : -e.AmountCents)
        + data.Transfers.Where(t => t.ToWalletId == wallet.Id).Sum(t => t.AmountCents)
        - data.Transfers.Where(t => t.FromWalletId == wallet.Id).Sum(t => t.AmountCents);

    [Fact]
    public async Task Soma_dos_saldos_atuais_e_igual_ao_saldo_final_Realizado_do_ultimo_mes_relevante()
    {
        using var anonymous = Factory.CreateClient();
        var lia = await anonymous.SignUpAsync(name: "Lia");
        using var http = Factory.CreateClient().WithBearer(lia.AccessToken);
        var data = Generate(seed: 20260601);
        await Factory.SeedAsync(lia, [.. data.Wallets, .. data.Entries, .. data.Transfers]);

        var lastRelevant = data.Entries.Where(e => e.Status == EntryStatus.Settled).Select(e => e.ReferenceYearMonth)
            .Concat(data.Wallets.Select(w => YearMonth.From(w.InitialBalanceDate)))
            .Max();
        var expected = data.Wallets.Sum(w => CurrentBalance(data, w));

        var wallets = await http.GetFromJsonAsync<JsonElement>("/api/v1/wallets?archived=all");
        wallets.EnumerateArray().Sum(w => w.GetProperty("currentBalanceCents").GetInt64()).ShouldBe(expected);

        var response = await http.GetAsync($"/api/v1/months/{lastRelevant}");
        response.EnsureSuccessStatusCode();
        var view = await response.Content.ReadFromJsonAsync<JsonElement>();

        view.GetProperty("actual").GetProperty("finalBalanceCents").GetInt64().ShouldBe(expected);

        // Meses seguintes não mudam nada: sem efetivados novos, o saldo final se mantém.
        var later = await http.GetFromJsonAsync<JsonElement>($"/api/v1/months/{lastRelevant.AddMonths(6)}");
        later.GetProperty("actual").GetProperty("finalBalanceCents").GetInt64().ShouldBe(expected);
        later.GetProperty("actual").GetProperty("openingBalanceCents").GetInt64().ShouldBe(expected);
    }
}
