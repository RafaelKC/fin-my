using FinMy.Domain.Common;

namespace FinMy.Domain.Idempotency;

/// <summary>
/// Resposta guardada de um <c>POST</c> de criação (<c>idempotency_keys</c>, research R2). PK <c>(tenant_id, key)</c>.
/// <see cref="StatusCode"/> 0 marca a requisição ainda em andamento.
/// </summary>
public sealed class IdempotencyKey : ITenantScoped
{
    /// <summary>Estado "em andamento": a linha é inserida antes de executar o endpoint.</summary>
    public const int InProgress = 0;

    // Construtor para o EF Core.
    private IdempotencyKey()
    {
    }

    public IdempotencyKey(Guid tenantId, Guid key, Guid? userId, byte[] requestHash, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Key = key;
        UserId = userId;
        RequestHash = requestHash;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; private set; }

    public Guid Key { get; private set; }

    public Guid? UserId { get; private set; }

    /// <summary>SHA-256 de <c>método + rota + corpo</c>.</summary>
    public byte[] RequestHash { get; private set; } = [];

    public int StatusCode { get; private set; } = InProgress;

    /// <summary>Corpo JSON da resposta 2xx (nulo quando não há corpo).</summary>
    public string? ResponseBody { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
