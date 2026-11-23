using System.Net;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Errors;

public class RateLimitTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    protected override IEndpointModule[] ExtraEndpoints => [new TestEndpoints()];

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["RateLimiting:Auth:PermitLimit"] = "3",
        ["RateLimiting:Auth:WindowSeconds"] = "60",
    };

    [Fact]
    public async Task Politica_auth_limita_por_IP_e_responde_429_problem_com_retry_after()
    {
        for (var i = 0; i < 3; i++)
        {
            (await Client.GetAsync("/test/limited")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var response = await Client.GetAsync("/test/limited");

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.ShouldNotBeNull();
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("rate_limited");
        json.RootElement.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("retryAfterSeconds").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Limite_e_por_IP_de_origem_conforme_X_Forwarded_For()
    {
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/test/limited");
            request.Headers.Add("X-Forwarded-For", "203.0.113.10");
            (await Client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var outro = new HttpRequestMessage(HttpMethod.Get, "/test/limited");
        outro.Headers.Add("X-Forwarded-For", "203.0.113.11");

        (await Client.SendAsync(outro)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_nao_entra_no_limite_da_politica_auth()
    {
        for (var i = 0; i < 6; i++)
        {
            (await Client.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
