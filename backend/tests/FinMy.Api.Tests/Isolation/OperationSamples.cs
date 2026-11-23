namespace FinMy.Api.Tests.Isolation;

/// <summary>Quem está chamando (B) e o que é de A, para montar a requisição de uma operação.</summary>
public sealed record SampleContext(string EmailOfB, string PasswordOfB)
{
    /// <summary>E-mail ainda não cadastrado (cada chamada devolve um novo).</summary>
    public static string NewEmail() => $"isolamento.{Guid.NewGuid():N}@exemplo.com";
}

/// <summary>
/// Corpos válidos das operações já conhecidas, para a chamada chegar à regra de negócio em vez de parar na validação.
/// Operação nova que não estiver aqui recebe um corpo gerado a partir do esquema (<see cref="SchemaSampler"/>);
/// ids de A em qualquer campo uuid são sempre injetados pela suíte.
/// </summary>
public static class OperationSamples
{
    public const string NewPassword = "nova-senha-456";

    public static IReadOnlyDictionary<string, Func<SampleContext, object>> Bodies { get; } =
        new Dictionary<string, Func<SampleContext, object>>
        {
            ["POST /api/v1/auth/signup"] = _ => new { name = "Conta Nova", email = SampleContext.NewEmail(), password = "senha-de-teste-123" },
            ["POST /api/v1/auth/login"] = c => new { email = c.EmailOfB, password = c.PasswordOfB },
            ["POST /api/v1/auth/password-reset/request"] = c => new { email = c.EmailOfB },
            ["POST /api/v1/auth/password-reset/confirm"] = _ => new { token = "token-que-nao-existe", newPassword = NewPassword },
            ["PATCH /api/v1/me"] = _ => new { name = "Nome Novo de B" },
            ["POST /api/v1/me/password"] = c => new { currentPassword = c.PasswordOfB, newPassword = NewPassword },
            ["PATCH /api/v1/tenant"] = _ => new { name = "Espaço Novo de B" },
            ["POST /api/v1/entries"] = _ => new { kind = "expense", description = "Tim", amount = "44,99", date = "2026-03-10", walletId = Guid.NewGuid() },
            ["PUT /api/v1/entries/{id}"] = _ => new { kind = "expense", description = "Tim", amount = "44,99", date = "2026-03-10", walletId = Guid.NewGuid(), status = "settled", version = 1 },
            ["PATCH /api/v1/entries/{id}/status"] = _ => new { status = "planned" },
            ["POST /api/v1/entries/{id}/duplicate"] = _ => new { date = "2026-04-10" },
            ["POST /api/v1/transfers"] = _ => new { fromWalletId = Guid.NewGuid(), toWalletId = Guid.NewGuid(), amount = "20,00", date = "2026-06-29" },
            ["PUT /api/v1/transfers/{id}"] = _ => new { fromWalletId = Guid.NewGuid(), toWalletId = Guid.NewGuid(), amount = "20,00", date = "2026-06-29", version = 1 },
        };
}
