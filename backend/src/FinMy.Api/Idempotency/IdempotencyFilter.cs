using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FinMy.Api.Errors;
using FinMy.Application.Abstractions;

namespace FinMy.Api.Idempotency;

/// <summary>
/// Filtro de endpoint do cabeçalho opcional <c>Idempotency-Key</c> (research R2; aplique com
/// <see cref="IdempotencyEndpointExtensions.WithIdempotency{TBuilder}"/>). Sem o cabeçalho, nada muda. Com ele:
/// UUID inválido → 422 <c>idempotency.invalid_key</c>; mesma chave e mesma requisição (método + rota + corpo) →
/// repete status e corpo gravados sem executar de novo; mesma chave com requisição diferente → 422
/// <c>idempotency.key_reused</c>; mesma chave ainda em execução → 409 <c>idempotency.in_progress</c>.
/// Só respostas 2xx ficam guardadas; as demais liberam a chave para o cliente tentar de novo.
/// </summary>
public sealed class IdempotencyFilter(IIdempotencyStore store, ICurrentContext current) : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotency-Replayed";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return await next(context);
        }

        if (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var key))
        {
            return ProblemResults.From(IdempotencyErrors.InvalidKey);
        }

        if (current.TenantId is null)
        {
            // Sem Espaço não há onde guardar a chave; a autorização do endpoint responde antes disso na prática.
            return await next(context);
        }

        var cancellationToken = http.RequestAborted;
        var begin = await store.BeginAsync(key, await HashRequestAsync(http.Request, cancellationToken), cancellationToken);

        switch (begin.Outcome)
        {
            case IdempotencyOutcome.Reused:
                return ProblemResults.From(IdempotencyErrors.KeyReused);
            case IdempotencyOutcome.InProgress:
                return ProblemResults.From(IdempotencyErrors.InProgress);
            case IdempotencyOutcome.Replay:
                http.Response.Headers[ReplayedHeaderName] = "true";
                return begin.ResponseBody is { Length: > 0 } body
                    ? Results.Text(body, "application/json", Encoding.UTF8, begin.StatusCode)
                    : Results.StatusCode(begin.StatusCode);
        }

        return await ExecuteAndRecordAsync(context, next, key);
    }

    private async Task<object?> ExecuteAndRecordAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next, Guid key)
    {
        var http = context.HttpContext;
        var original = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            var result = await next(context);
            await (result as IResult ?? Results.Ok(result)).ExecuteAsync(http);
        }
        catch
        {
            http.Response.Body = original;
            await store.AbandonAsync(key, CancellationToken.None);
            throw;
        }

        http.Response.Body = original;
        var status = http.Response.StatusCode;
        if (status is >= 200 and < 300)
        {
            var isJson = http.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
            var body = isJson && buffer.Length > 0 ? Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length) : null;
            await store.CompleteAsync(key, status, body, CancellationToken.None);
        }
        else
        {
            await store.AbandonAsync(key, CancellationToken.None);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, http.RequestAborted);
        return AlreadyWritten.Instance;
    }

    // SHA-256 de método + rota (caminho e query) + corpo; o corpo já foi lido pelo binding, mas o buffer ligado
    // por UseIdempotencyBuffering permite relê-lo daqui.
    private static async Task<byte[]> HashRequestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.Body.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{request.Method}\n{request.Path}{request.QueryString}\n")));

        var chunk = new byte[4096];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            hash.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return hash.GetHashAndReset();
    }

    /// <summary>A resposta já foi escrita no corpo real; o framework não deve escrever mais nada.</summary>
    private sealed class AlreadyWritten : IResult
    {
        public static readonly AlreadyWritten Instance = new();

        public Task ExecuteAsync(HttpContext httpContext) => Task.CompletedTask;
    }
}
