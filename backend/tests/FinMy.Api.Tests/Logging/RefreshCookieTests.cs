using System.Net;
using System.Net.Http.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Logging;

/// <summary>
/// Atributos do cookie <c>finmy_rt</c> (contracts/openapi.yaml <c>RefreshCookie</c>), definido pelo cadastro (T019):
/// HttpOnly, Secure, SameSite=Strict, Path e Max-Age.
/// </summary>
public class RefreshCookieTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task Cookie_finmy_rt_e_httponly_secure_samesite_strict_com_path_e_max_age()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/signup", new { name = "Maria Teste", email = $"maria.{Guid.NewGuid():N}@exemplo.com", password = AccountHelpers.DefaultPassword });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("finmy_rt=", StringComparison.Ordinal));

        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/v1/auth", Case.Insensitive);
        cookie.ShouldContain("max-age=7776000", Case.Insensitive);
    }
}
