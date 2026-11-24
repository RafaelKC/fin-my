using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinMy.Api.Idempotency;
using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using ModelContextProtocol.Protocol;

namespace FinMy.Api.Mcp;

/// <summary>
/// <c>requestId</c> das ferramentas de criação como <c>Idempotency-Key</c> (008 research R4): mesma tabela e serviço da API REST
/// (002 R2). Mesmo <c>requestId</c> com os mesmos argumentos devolve a resposta guardada (<c>replayed: true</c>) sem criar de novo;
/// com argumentos diferentes → <c>idempotency.key_reused</c>. Sem <c>requestId</c>, nada muda.
/// </summary>
public sealed class McpIdempotency(IIdempotencyStore store, ICurrentContext current)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CallToolResult> RunAsync(string? requestId, string tool, object arguments, Func<Task<object>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (string.IsNullOrWhiteSpace(requestId) || current.TenantId is null)
        {
            return await McpResult.RunAsync(work);
        }

        if (!Guid.TryParseExact(requestId.Trim(), "D", out var key))
        {
            return McpResult.Error(new DomainError(
                IdempotencyErrors.InvalidKey.Code,
                "O requestId deve ser um UUID válido.",
                DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["requestId"] = ["O requestId deve ser um UUID válido."] }));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(tool + "\n" + JsonSerializer.Serialize(arguments, Json)));
        var begin = await store.BeginAsync(key, hash, cancellationToken);
        switch (begin.Outcome)
        {
            case IdempotencyOutcome.Reused:
                return McpResult.Error(IdempotencyErrors.KeyReused);
            case IdempotencyOutcome.InProgress:
                return McpResult.Error(IdempotencyErrors.InProgress);
            case IdempotencyOutcome.Replay:
                return McpResult.Replay(begin.ResponseBody);
        }

        object result;
        try
        {
            result = await work();
        }
        catch (DomainException ex)
        {
            // Falha de regra de negócio não fica guardada: a pessoa corrige e tenta de novo com o mesmo requestId.
            await store.AbandonAsync(key, CancellationToken.None);
            return McpResult.Error(ex.Error);
        }
        catch
        {
            await store.AbandonAsync(key, CancellationToken.None);
            throw;
        }

        await store.CompleteAsync(key, 200, JsonSerializer.Serialize(result, Json), CancellationToken.None);
        return McpResult.Ok(result);
    }
}
