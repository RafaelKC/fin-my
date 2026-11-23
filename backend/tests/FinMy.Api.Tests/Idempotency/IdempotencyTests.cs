using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Idempotency;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FinMy.Api.Tests.Idempotency;

/// <summary>
/// <c>.WithIdempotency()</c> (research R2) num <c>POST</c> de teste que "cria" um item e conta quantas vezes executou:
/// mesma chave devolve a mesma resposta sem duplicar, corpo diferente é 422, chave inválida é 422 e Espaços não colidem.
/// </summary>
public class IdempotencyTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private readonly CreateItemEndpoint _endpoint = new();

    protected override IEndpointModule[] ExtraEndpoints => [_endpoint];

    private async Task<HttpClient> NewUserAsync(string name)
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        return Factory.CreateClient().WithBearer(account.AccessToken);
    }

    private static HttpRequestMessage Post(string body, string? key, string url = "/test/idempotent/items")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyFilter.HeaderName, key);
        }

        return request;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Mesma_chave_e_mesmo_corpo_devolve_a_mesma_resposta_sem_duplicar()
    {
        var lia = await NewUserAsync("Lia");
        var key = Guid.NewGuid().ToString();

        var first = await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key));
        var second = await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key));

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Headers.Contains(IdempotencyFilter.ReplayedHeaderName).ShouldBeFalse();
        second.Headers.GetValues(IdempotencyFilter.ReplayedHeaderName).ShouldBe(["true"]);
        var firstBody = await JsonAsync(first);
        var secondBody = await JsonAsync(second);
        secondBody.GetProperty("id").GetGuid().ShouldBe(firstBody.GetProperty("id").GetGuid());
        secondBody.GetProperty("number").GetInt32().ShouldBe(1);
        _endpoint.Executions.ShouldBe(1);
    }

    [Fact]
    public async Task Sem_o_cabecalho_cada_requisicao_executa()
    {
        var lia = await NewUserAsync("Lia");

        await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key: null));
        await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key: null));

        _endpoint.Executions.ShouldBe(2);
    }

    [Fact]
    public async Task Mesma_chave_com_corpo_diferente_responde_422_key_reused()
    {
        var lia = await NewUserAsync("Lia");
        var key = Guid.NewGuid().ToString();
        await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key));

        var response = await lia.SendAsync(Post("""{"name":"Farmácia do Léo"}""", key));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("code").GetString().ShouldBe("idempotency.key_reused");
        _endpoint.Executions.ShouldBe(1);
    }

    [Fact]
    public async Task Mesma_chave_em_outra_rota_responde_422_key_reused()
    {
        var lia = await NewUserAsync("Lia");
        var key = Guid.NewGuid().ToString();
        await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key));

        var response = await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key, "/test/idempotent/other"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("code").GetString().ShouldBe("idempotency.key_reused");
    }

    [Theory]
    [InlineData("nao-e-uuid")]
    [InlineData(" ")]
    [InlineData("{3f2504e0-4f89-11d3-9a0c-0305e82c3301}")]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301")]
    public async Task Chave_que_nao_e_UUID_responde_422_invalid_key(string key)
    {
        var lia = await NewUserAsync("Lia");

        var response = await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("code").GetString().ShouldBe("idempotency.invalid_key");
        _endpoint.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task Chave_de_outro_Espaco_nao_colide()
    {
        var lia = await NewUserAsync("Lia");
        var leo = await NewUserAsync("Léo");
        var key = Guid.NewGuid().ToString();

        var fromLia = await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key));
        var fromLeo = await leo.SendAsync(Post("""{"name":"Farmácia do Léo"}""", key));

        fromLeo.StatusCode.ShouldBe(HttpStatusCode.Created);
        var leoBody = await JsonAsync(fromLeo);
        leoBody.GetProperty("name").GetString().ShouldBe("Farmácia do Léo");
        leoBody.GetProperty("number").GetInt32().ShouldNotBe((await JsonAsync(fromLia)).GetProperty("number").GetInt32());
        _endpoint.Executions.ShouldBe(2);
    }

    [Fact]
    public async Task Resposta_de_erro_nao_fica_guardada_e_a_chave_pode_ser_reusada()
    {
        var lia = await NewUserAsync("Lia");
        var key = Guid.NewGuid().ToString();

        var rejected = await lia.SendAsync(Post("""{"name":""}""", key));
        var accepted = await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key));

        rejected.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Excecao_nao_deixa_a_chave_presa()
    {
        var lia = await NewUserAsync("Lia");
        var key = Guid.NewGuid().ToString();

        var failed = await lia.SendAsync(Post("""{"name":"boom"}""", key));
        var retried = await lia.SendAsync(Post("""{"name":"Mercado da Lia"}""", key));

        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        retried.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Duas_requisicoes_simultaneas_com_a_mesma_chave_executam_uma_so_vez()
    {
        var lia = await NewUserAsync("Lia");
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => lia.SendAsync(Post("""{"name":"slow"}""", key))));

        _endpoint.Executions.ShouldBe(1);
        responses.Select(r => r.StatusCode).Order().ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        (await JsonAsync(conflict)).GetProperty("code").GetString().ShouldBe("idempotency.in_progress");
    }

    [Fact]
    public async Task Resposta_sem_corpo_tambem_e_repetida()
    {
        var lia = await NewUserAsync("Lia");
        var key = Guid.NewGuid().ToString();

        var first = await lia.SendAsync(Post("{}", key, "/test/idempotent/empty"));
        var second = await lia.SendAsync(Post("{}", key, "/test/idempotent/empty"));

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        _endpoint.Executions.ShouldBe(1);
    }

    /// <summary>Endpoints de teste: cada execução real incrementa o contador, o que revela duplicação.</summary>
    private sealed class CreateItemEndpoint : IEndpointModule
    {
        private int _executions;

        public int Executions => Volatile.Read(ref _executions);

        public void MapEndpoints(IEndpointRouteBuilder app)
        {
            app.MapPost("/test/idempotent/items", async (ItemBody body) =>
            {
                if (string.IsNullOrWhiteSpace(body.Name))
                {
                    return Results.UnprocessableEntity(new { code = "validation_failed" });
                }

                var number = Interlocked.Increment(ref _executions);
                if (body.Name == "boom")
                {
                    throw new InvalidOperationException("falha de teste");
                }

                if (body.Name == "slow")
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }

                return Results.Json(new { number, body.Name, id = Guid.NewGuid() }, statusCode: StatusCodes.Status201Created);
            }).RequireAuthorization().WithIdempotency();

            app.MapPost("/test/idempotent/other", (ItemBody body) => Results.Ok(body)).RequireAuthorization().WithIdempotency();

            app.MapPost("/test/idempotent/empty", () =>
            {
                Interlocked.Increment(ref _executions);
                return Results.NoContent();
            }).RequireAuthorization().WithIdempotency();
        }
    }

    private sealed record ItemBody(string Name);
}
