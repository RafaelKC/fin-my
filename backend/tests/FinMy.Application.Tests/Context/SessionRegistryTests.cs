using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Auth;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FinMy.Application.Tests.Context;

[Collection(PostgresTestGroup.Name)]
public sealed class SessionRegistryTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FakeClock _clock = new(Start);
    private readonly List<FinMyDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var db in _contexts)
        {
            db.Dispose();
        }

        _cache.Dispose();
    }

    private (SessionRegistry Registry, RefreshTokenStore Store) New()
    {
        var db = postgres.CreateContext(new CurrentContext(), _clock);
        _contexts.Add(db);
        var registry = new SessionRegistry(db, _clock, _cache);
        return (registry, new RefreshTokenStore(db, _clock, registry));
    }

    private async Task<Guid> SeedUserAsync()
    {
        await using var db = postgres.CreateContext(new CurrentContext());
        var user = new User("Maria", $"maria-{Guid.NewGuid():N}@example.com", "hash", Start);
        db.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Sessao_com_refresh_token_ativo_esta_ativa()
    {
        var (registry, store) = New();
        var issued = await store.IssueAsync(await SeedUserAsync(), null, null, CancellationToken.None);

        (await registry.IsActiveAsync(issued.SessionId, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task Sessao_desconhecida_nao_esta_ativa()
    {
        var (registry, _) = New();

        (await registry.IsActiveAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Revogar_a_sessao_vale_na_hora_mesmo_com_a_sessao_em_cache()
    {
        var (registry, store) = New();
        var issued = await store.IssueAsync(await SeedUserAsync(), null, null, CancellationToken.None);
        (await registry.IsActiveAsync(issued.SessionId, CancellationToken.None)).ShouldBeTrue();

        await store.RevokeSessionAsync(issued.SessionId, RefreshTokenRevocationReason.Logout, CancellationToken.None);

        (await registry.IsActiveAsync(issued.SessionId, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Sessao_ativa_fica_em_cache_ate_ser_esquecida()
    {
        var (registry, store) = New();
        var issued = await store.IssueAsync(await SeedUserAsync(), null, null, CancellationToken.None);
        (await registry.IsActiveAsync(issued.SessionId, CancellationToken.None)).ShouldBeTrue();

        // Revogação por fora do store (sem invalidar): o cache ainda responde.
        await using (var other = postgres.CreateContext(new CurrentContext()))
        {
            await other.Set<RefreshToken>().Where(r => r.FamilyId == issued.SessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, Start));
        }

        (await registry.IsActiveAsync(issued.SessionId, CancellationToken.None)).ShouldBeTrue();

        registry.Forget(issued.SessionId);

        (await registry.IsActiveAsync(issued.SessionId, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Rotacao_mantem_a_sessao_ativa()
    {
        var (registry, store) = New();
        var issued = await store.IssueAsync(await SeedUserAsync(), null, null, CancellationToken.None);
        (await registry.IsActiveAsync(issued.SessionId, CancellationToken.None)).ShouldBeTrue();

        (await store.RotateAsync(issued.Token, CancellationToken.None)).Status.ShouldBe(FinMy.Application.Abstractions.RefreshRotationStatus.Rotated);

        (await registry.IsActiveAsync(issued.SessionId, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task Sessao_cujo_refresh_token_venceu_nao_esta_ativa()
    {
        var (registry, store) = New();
        var issued = await store.IssueAsync(await SeedUserAsync(), null, null, CancellationToken.None);

        _clock.UtcNow = Start.Add(RefreshTokenStore.Lifetime).AddSeconds(1);

        (await registry.IsActiveAsync(issued.SessionId, CancellationToken.None)).ShouldBeFalse();
    }
}
