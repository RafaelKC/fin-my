using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Infrastructure.Context;

/// <summary>Implementação scoped de <see cref="ICurrentContext"/>.</summary>
public sealed class CurrentContext : ICurrentContext
{
    public Guid? UserId { get; private set; }

    public Guid? TenantId { get; private set; }

    public Channel Channel { get; private set; } = Channel.App;

    public bool IsAuthenticated => UserId is not null && !IsSystem;

    public bool IsSystem { get; private set; }

    public Guid TenantIdOrEmpty => TenantId ?? Guid.Empty;

    public Guid DeletionBatchId { get; private set; } = Guid.CreateVersion7();

    public Guid BeginDeletionBatch() => DeletionBatchId = Guid.CreateVersion7();

    /// <summary>Preenche o contexto a partir de claims já validadas (usado pelo middleware).</summary>
    public void Populate(Guid userId, Guid tenantId, Channel channel)
    {
        UserId = userId;
        TenantId = tenantId;
        Channel = channel;
        IsSystem = false;
    }

    public IDisposable BeginSystemScope(Guid tenantId)
    {
        var previous = (UserId, TenantId, Channel, IsSystem);

        UserId = SystemUser.Id;
        TenantId = tenantId;
        Channel = Channel.System;
        IsSystem = true;

        return new Scope(() => (UserId, TenantId, Channel, IsSystem) = previous);
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        private Action? _restore = restore;

        public void Dispose()
        {
            _restore?.Invoke();
            _restore = null;
        }
    }
}
