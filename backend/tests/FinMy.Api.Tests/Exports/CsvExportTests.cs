using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Exports;

/// <summary>
/// <c>GET /api/v1/exports/entries.csv</c> (006 T004, research R4): cabeçalho e formato do arquivo, as colunas de cada tipo de
/// lançamento, o mês em que cada um entra no período (US4.1, US4.2), o intervalo e o isolamento entre Espaços.
/// </summary>
public class CsvExportTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Header = "Tipo;Descrição;Data;Mês de referência;Valor;Situação;Carteira;Cartão;Fatura;Parcela;Tags";

    private static async Task<Guid> PostAsync(HttpClient http, string url, object body)
    {
        var response = await http.PostAsJsonAsync(url, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<Guid> WalletAsync(HttpClient http, string name, string initial = "1000") =>
        PostAsync(http, "/api/v1/wallets", new { name, type = "checking", color = "#1E88E5", initialBalance = initial, initialBalanceDate = "2026-01-01" });

    private static Task<Guid> EntryAsync(
        HttpClient http, Guid walletId, string kind, string description, string amount, string date, string status, string? referenceMonth = null, params Guid[] tagIds) =>
        PostAsync(http, "/api/v1/entries", new { kind, description, amount, date, walletId, status, referenceMonth, tagIds });

    private static async Task PurchaseAsync(
        HttpClient http, Guid cardId, string description, string amount, string date, string nature = "debit", object? installments = null, params Guid[] tagIds)
    {
        var response = await http.PostAsJsonAsync("/api/v1/card-purchases", new { description, amount, date, nature, creditCardId = cardId, installments, tagIds });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<(HttpResponseMessage Response, string Text)> ExportAsync(HttpClient http, string from, string to)
    {
        var response = await http.GetAsync($"/api/v1/exports/entries.csv?from={from}&to={to}");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return (response, Encoding.UTF8.GetString(bytes));
    }

    private static async Task<List<string>> LinesAsync(HttpClient http, string from, string to)
    {
        var (response, text) = await ExportAsync(http, from, to);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        var lines = text.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        lines[0].ShouldBe(Header);
        return lines[1..];
    }

    [Fact]
    public async Task Arquivo_tem_cabecalho_exato_BOM_UTF8_tipo_e_nome_com_o_periodo()
    {
        var (http, _) = await NewSpaceAsync(Factory);

        var response = await http.GetAsync("/api/v1/exports/entries.csv?from=2026-03&to=2026-05");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().ShouldBe("text/csv; charset=utf-8");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("fin-my-2026-03_2026-05.csv");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).ShouldBe(Header + "\r\n");
    }

    [Fact]
    public async Task US4_1_entrada_e_saida_saem_com_valor_com_sinal_situacao_carteira_e_tags()
    {
        var (http, _) = await NewSpaceAsync(Factory);
        var azul = await WalletAsync(http, "Azul");
        var casa = await PostAsync(http, "/api/v1/tags", new { name = "Casa" });
        var bruna = await PostAsync(http, "/api/v1/tags", new { name = "Bruna", isSettlement = true });
        await EntryAsync(http, azul, "income", "Salário", "1234,56", "2026-03-05", "settled", null, casa);
        await EntryAsync(http, azul, "expense", "Aluguel", "1500", "2026-03-10", "planned", null, bruna, casa);
        await EntryAsync(http, azul, "expense", "Luz", "80,05", "2026-03-12", "settled");
        await EntryAsync(http, azul, "income", "Freela", "300", "2026-03-20", "planned");

        var lines = await LinesAsync(http, "2026-03", "2026-03");

        lines.ShouldBe(
        [
            "Entrada;Salário;05/03/2026;03/2026;1234,56;Recebida;Azul;;;;Casa",
            "Saída;Aluguel;10/03/2026;03/2026;-1500,00;Prevista;Azul;;;;Bruna, Casa",
            "Saída;Luz;12/03/2026;03/2026;-80,05;Paga;Azul;;;;",
            "Entrada;Freela;20/03/2026;03/2026;300,00;Prevista;Azul;;;;",
        ]);
    }

    [Fact]
    public async Task US4_2_entrada_e_saida_entram_pelo_mes_de_referencia_e_nao_pela_data()
    {
        var (http, _) = await NewSpaceAsync(Factory);
        var azul = await WalletAsync(http, "Azul");
        await EntryAsync(http, azul, "expense", "Seguro", "100", "2026-03-28", "planned", "2026-04");
        await EntryAsync(http, azul, "expense", "Água", "50", "2026-04-02", "settled", "2026-03");

        (await LinesAsync(http, "2026-03", "2026-03")).ShouldBe(["Saída;Água;02/04/2026;03/2026;-50,00;Paga;Azul;;;;"]);
        (await LinesAsync(http, "2026-04", "2026-04")).ShouldBe(["Saída;Seguro;28/03/2026;04/2026;-100,00;Prevista;Azul;;;;"]);
        (await LinesAsync(http, "2026-03", "2026-04")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task US4_1_compra_sai_pelo_vencimento_da_fatura_sem_mes_de_referencia_com_cartao_fatura_e_parcela()
    {
        var (http, _) = await NewSpaceAsync(Factory);
        var azul = await WalletAsync(http, "Azul");
        var cartao = await CreateCardAsync(http, "Verde", azul); // fecha 18, vence 25
        var bruna = await PostAsync(http, "/api/v1/tags", new { name = "Bruna", isSettlement = true });
        await PurchaseAsync(http, cartao.Id, "Mercado", "200,50", "2026-03-10", tagIds: bruna);
        await PurchaseAsync(http, cartao.Id, "Estorno", "20", "2026-03-11", "credit");
        await PurchaseAsync(http, cartao.Id, "Geladeira", "300", "2026-03-12", installments: new { total = 3, mode = "total_amount" });
        await PurchaseAsync(http, cartao.Id, "Cinema", "40", "2026-03-20"); // fecha dia 18: vence em abril

        var march = await LinesAsync(http, "2026-03", "2026-03");
        var april = await LinesAsync(http, "2026-04", "2026-04");

        march.ShouldBe(
        [
            "Compra;Mercado;10/03/2026;;-200,50;;Azul;Verde;03/2026;;Bruna",
            "Compra;Estorno;11/03/2026;;20,00;;Azul;Verde;03/2026;;",
            "Compra;Geladeira;12/03/2026;;-100,00;;Azul;Verde;03/2026;1/3;",
        ]);
        april.ShouldContain("Compra;Cinema;20/03/2026;;-40,00;;Azul;Verde;04/2026;;");
        april.ShouldContain("Compra;Geladeira;12/03/2026;;-100,00;;Azul;Verde;04/2026;2/3;");
    }

    [Fact]
    public async Task US4_1_pagamento_de_fatura_sai_no_mes_de_vencimento_mesmo_pago_em_outro_mes()
    {
        var (http, _) = await NewSpaceAsync(Factory);
        var azul = await WalletAsync(http, "Azul");
        var cartao = await CreateCardAsync(http, "Verde", azul);
        await PurchaseAsync(http, cartao.Id, "Mercado", "200", "2026-03-10");
        (await http.PostAsJsonAsync($"{CardsUrl}/{cartao.Id}/invoices/2026-03/payments", new { amount = "150", walletId = azul, date = "2026-04-02" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var march = await LinesAsync(http, "2026-03", "2026-03");

        march.ShouldBe(
        [
            "Compra;Mercado;10/03/2026;;-200,00;;Azul;Verde;03/2026;;",
            "Pagamento de fatura;Pagamento da fatura Verde;02/04/2026;03/2026;-150,00;Paga;Azul;Verde;03/2026;;",
        ]);
        (await LinesAsync(http, "2026-04", "2026-04")).ShouldBeEmpty();
    }

    [Fact]
    public async Task US4_2_transferencia_sai_pelo_mes_da_data_com_origem_e_destino_e_valor_positivo()
    {
        var (http, _) = await NewSpaceAsync(Factory);
        var azul = await WalletAsync(http, "Azul");
        var verde = await WalletAsync(http, "Verde");
        await PostAsync(http, "/api/v1/transfers", new { fromWalletId = azul, toWalletId = verde, amount = "250", date = "2026-03-31", description = "Reserva" });

        (await LinesAsync(http, "2026-03", "2026-03")).ShouldBe(["Transferência;Reserva;31/03/2026;;250,00;Efetivada;Azul → Verde;;;;"]);
        (await LinesAsync(http, "2026-04", "2026-04")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Linhas_seguem_a_ordem_de_mes_data_e_criacao()
    {
        var (http, _) = await NewSpaceAsync(Factory);
        var azul = await WalletAsync(http, "Azul");
        var verde = await WalletAsync(http, "Verde");
        await EntryAsync(http, azul, "expense", "Segunda", "1", "2026-03-15", "settled");
        await EntryAsync(http, azul, "expense", "Primeira", "1", "2026-03-02", "settled");
        await EntryAsync(http, azul, "expense", "Empate A", "1", "2026-03-15", "settled");
        await PostAsync(http, "/api/v1/transfers", new { fromWalletId = azul, toWalletId = verde, amount = "1", date = "2026-03-10", description = "Meio" });
        await EntryAsync(http, azul, "expense", "Abril", "1", "2026-04-01", "settled");

        var lines = await LinesAsync(http, "2026-03", "2026-04");

        lines.Select(l => l.Split(';')[1]).ShouldBe(["Primeira", "Meio", "Segunda", "Empate A", "Abril"]);
    }

    [Fact]
    public async Task Campos_com_ponto_e_virgula_aspas_ou_quebra_de_linha_vao_entre_aspas()
    {
        var (http, _) = await NewSpaceAsync(Factory);
        var azul = await WalletAsync(http, "Azul");
        await EntryAsync(http, azul, "expense", "Pão; \"queijo\"", "10", "2026-03-05", "settled");

        var (_, text) = await ExportAsync(http, "2026-03", "2026-03");

        text.ShouldContain("Saída;\"Pão; \"\"queijo\"\"\";05/03/2026;03/2026;-10,00;Paga;Azul;;;;\r\n");
    }

    [Fact]
    public async Task Exporta_apenas_o_Espaco_de_quem_pede()
    {
        var (lia, _) = await NewSpaceAsync(Factory, "Lia");
        var (leo, _) = await NewSpaceAsync(Factory, "Léo");
        await EntryAsync(lia, await WalletAsync(lia, "Azul"), "expense", "Da Lia", "10", "2026-03-05", "settled");
        await EntryAsync(leo, await WalletAsync(leo, "Verde"), "expense", "Do Léo", "20", "2026-03-05", "settled");

        (await LinesAsync(lia, "2026-03", "2026-03")).ShouldBe(["Saída;Da Lia;05/03/2026;03/2026;-10,00;Paga;Azul;;;;"]);
        (await LinesAsync(leo, "2026-03", "2026-03")).ShouldBe(["Saída;Do Léo;05/03/2026;03/2026;-20,00;Paga;Verde;;;;"]);
    }

    [Theory]
    [InlineData("2026-03", "2026-02", "export.range_invalid")]
    [InlineData("2026-13", "2026-14", "export.range_invalid")]
    [InlineData("março", "2026-03", "export.range_invalid")]
    [InlineData("1999-12", "2026-03", "export.range_invalid")]
    [InlineData("2026-03", "2101-01", "export.range_invalid")]
    [InlineData("2016-01", "2026-01", "export.range_too_long")]
    public async Task Intervalo_invalido_ou_maior_que_120_meses_devolve_422(string from, string to, string code)
    {
        var (http, _) = await NewSpaceAsync(Factory);

        var (response, text) = await ExportAsync(http, from, to);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, text);
        text.ShouldContain(code);
    }

    [Fact]
    public async Task Intervalo_de_exatamente_120_meses_e_aceito()
    {
        var (http, _) = await NewSpaceAsync(Factory);

        (await ExportAsync(http, "2016-01", "2025-12")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sem_parametros_devolve_422()
    {
        var (http, _) = await NewSpaceAsync(Factory);

        (await http.GetAsync("/api/v1/exports/entries.csv")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Sem_login_devolve_401()
    {
        using var anonymous = Factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/exports/entries.csv?from=2026-03&to=2026-03")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
