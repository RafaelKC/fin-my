using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FinMy.Api.Tests.Support;

public sealed record UserDto(Guid Id, string Name, string Email);

public sealed record TenantDto(Guid Id, string Name, string Role);

/// <summary>Resposta de <c>signup</c>/<c>login</c>/<c>refresh</c> (contracts/openapi.yaml <c>SessionResponse</c>).</summary>
public sealed record SessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, UserDto User, TenantDto Tenant);

/// <summary>Conta criada pelos helpers de teste, com o token de acesso já obtido.</summary>
public sealed record TestAccount(string Email, string Password, SessionResponse Session)
{
    public string AccessToken => Session.AccessToken;
}

/// <summary>
/// Helpers para as fases seguintes: criar conta e obter token pelo contrato HTTP
/// (<c>POST /api/v1/auth/signup</c> e <c>/login</c>), sem tocar no banco.
/// </summary>
public static class AccountHelpers
{
    public const string DefaultPassword = "senha-de-teste-123";

    /// <summary>Cria uma conta (e o Espaço dela) e devolve a sessão. O e-mail é único por chamada se não informado.</summary>
    public static async Task<TestAccount> SignUpAsync(this HttpClient client, string? name = null, string? email = null, string password = DefaultPassword)
    {
        ArgumentNullException.ThrowIfNull(client);

        name ??= "Maria Teste";
        email ??= $"maria.{Guid.NewGuid():N}@exemplo.com";

        var response = await client.PostAsJsonAsync("/api/v1/auth/signup", new { name, email, password });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Resposta de signup vazia.");

        return new TestAccount(email, password, session);
    }

    /// <summary>Entra com uma conta existente e devolve a sessão.</summary>
    public static async Task<SessionResponse> LoginAsync(this HttpClient client, string email, string password = DefaultPassword)
    {
        ArgumentNullException.ThrowIfNull(client);

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Resposta de login vazia.");
    }

    /// <summary>Define o access token (Bearer) nas próximas requisições do cliente.</summary>
    public static HttpClient WithBearer(this HttpClient client, string accessToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Cria uma conta nova e devolve um cliente já autenticado como ela (cada conta, um Espaço).</summary>
    public static async Task<(HttpClient Client, TestAccount Account)> CreateAuthenticatedClientAsync(this ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var anonymous = factory.CreateClient();
        var account = await anonymous.SignUpAsync();

        var client = factory.CreateClient().WithBearer(account.AccessToken);
        return (client, account);
    }
}

internal static class JsonDefaults
{
    public static System.Text.Json.JsonSerializerOptions Options { get; } = new(System.Text.Json.JsonSerializerDefaults.Web);
}
