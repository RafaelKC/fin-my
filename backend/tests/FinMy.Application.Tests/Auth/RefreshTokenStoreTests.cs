using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Auth;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Application.Tests.Auth;

[Collection(PostgresTestGroup.Name)]
public class RefreshTokenStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SeedUserAsync()
    {
        await using var db = postgres.CreateContext(new CurrentContext());
        var user = new User("Maria", $"maria-{Guid.NewGuid():N}@example.com", "hash", Start);
        db.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private (RefreshTokenStore Store, FinMyDbContext Db, FakeClock Clock) NewStore(FakeClock? clock = null)
    {
        clock ??= new FakeClock(Start);
        var db = postgres.CreateContext(new CurrentContext(), clock);
        return (new RefreshTokenStore(db, clock), db, clock);
    }

    private async Task<RefreshToken> LoadAsync(string token)
    {
        await using var db = postgres.CreateContext(new CurrentContext());
        var hash = RefreshTokenStore.Hash(token);
        return await db.Set<RefreshToken>().AsNoTracking().SingleAsync(r => r.TokenHash == hash);
    }

    [Fact]
    public void Token_gerado_tem_256_bits_em_base64url_e_nao_se_repete()
    {
        var a = RefreshTokenStore.GenerateToken();
        var b = RefreshTokenStore.GenerateToken();

        a.ShouldNotBe(b);
        a.Length.ShouldBe(43); // 32 bytes em base64 sem padding
        a.ShouldMatch("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public async Task Emitir_guarda_so_o_hash_sha256_e_abre_sessao_de_90_dias()
    {
        var userId = await SeedUserAsync();
        var (store, db, _) = NewStore();
        await using var _db = db;

        var issued = await store.IssueAsync(userId, null, "  iPhone da Maria  ", CancellationToken.None);

        issued.ExpiresAt.ShouldBe(Start.AddDays(90));
        issued.SessionId.ShouldNotBe(Guid.Empty);
        var saved = await LoadAsync(issued.Token);
        saved.TokenHash.Length.ShouldBe(32);
        saved.TokenHash.ShouldBe(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(issued.Token)));
        saved.FamilyId.ShouldBe(issued.SessionId);
        saved.UserId.ShouldBe(userId);
        saved.DeviceLabel.ShouldBe("iPhone da Maria");
        saved.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Rotacao_troca_o_token_na_mesma_sessao_e_prolonga_a_validade()
    {
        var userId = await SeedUserAsync();
        var (store, db, clock) = NewStore();
        await using var _db = db;
        var first = await store.IssueAsync(userId, null, "celular", CancellationToken.None);

        clock.UtcNow = Start.AddDays(30);
        var rotation = await store.RotateAsync(first.Token, CancellationToken.None);

        rotation.Status.ShouldBe(RefreshRotationStatus.Rotated);
        rotation.UserId.ShouldBe(userId);
        rotation.Token.ShouldNotBeNull();
        rotation.Token.Token.ShouldNotBe(first.Token);
        rotation.Token.SessionId.ShouldBe(first.SessionId);
        rotation.Token.ExpiresAt.ShouldBe(Start.AddDays(120));

        var old = await LoadAsync(first.Token);
        var current = await LoadAsync(rotation.Token.Token);
        old.RevokedReason.ShouldBe(RefreshTokenRevocationReason.Rotated);
        old.ReplacedById.ShouldBe(current.Id);
        current.RevokedAt.ShouldBeNull();
        current.DeviceLabel.ShouldBe("celular");
    }

    [Fact]
    public async Task Token_desconhecido_e_invalido()
    {
        var (store, db, _) = NewStore();
        await using var _db = db;

        var rotation = await store.RotateAsync(RefreshTokenStore.GenerateToken(), CancellationToken.None);

        rotation.Status.ShouldBe(RefreshRotationStatus.Invalid);
        rotation.Token.ShouldBeNull();
    }

    [Fact]
    public async Task Token_apos_90_dias_sem_uso_expirou()
    {
        var userId = await SeedUserAsync();
        var (store, db, clock) = NewStore();
        await using var _db = db;
        var issued = await store.IssueAsync(userId, null, null, CancellationToken.None);

        clock.UtcNow = Start.AddDays(90);
        var rotation = await store.RotateAsync(issued.Token, CancellationToken.None);

        rotation.Status.ShouldBe(RefreshRotationStatus.Expired);
        rotation.Token.ShouldBeNull();
    }

    [Fact]
    public async Task Reuso_de_token_ja_trocado_revoga_a_sessao_inteira()
    {
        var userId = await SeedUserAsync();
        var (store, db, _) = NewStore();
        await using var _db = db;
        var first = await store.IssueAsync(userId, null, null, CancellationToken.None);
        var second = (await store.RotateAsync(first.Token, CancellationToken.None)).Token!;

        var reuse = await store.RotateAsync(first.Token, CancellationToken.None);

        reuse.Status.ShouldBe(RefreshRotationStatus.ReuseDetected);
        (await LoadAsync(second.Token)).RevokedReason.ShouldBe(RefreshTokenRevocationReason.ReuseDetected);
        (await store.RotateAsync(second.Token, CancellationToken.None)).Status.ShouldBe(RefreshRotationStatus.Revoked);
    }

    [Fact]
    public async Task Duas_trocas_simultaneas_do_mesmo_token_deixam_a_sessao_revogada()
    {
        var userId = await SeedUserAsync();
        var (seedStore, seedDb, clock) = NewStore();
        await using var _seed = seedDb;
        var issued = await seedStore.IssueAsync(userId, null, null, CancellationToken.None);

        var (a, dbA, _) = NewStore(clock);
        var (b, dbB, _) = NewStore(clock);
        await using var _a = dbA;
        await using var _b = dbB;
        var results = await Task.WhenAll(
            a.RotateAsync(issued.Token, CancellationToken.None),
            b.RotateAsync(issued.Token, CancellationToken.None));

        results.Count(r => r.Status == RefreshRotationStatus.Rotated).ShouldBeLessThanOrEqualTo(1);
        await using var check = postgres.CreateContext(new CurrentContext());
        (await check.Set<RefreshToken>().CountAsync(r => r.FamilyId == issued.SessionId && r.RevokedAt == null))
            .ShouldBe(results.Any(r => r.Status == RefreshRotationStatus.ReuseDetected) ? 0 : 1);
    }

    [Fact]
    public async Task Encerrar_sessao_revoga_os_tokens_ativos_e_a_troca_passa_a_dizer_revogada()
    {
        var userId = await SeedUserAsync();
        var (store, db, _) = NewStore();
        await using var _db = db;
        var outra = await store.IssueAsync(userId, null, "outro aparelho", CancellationToken.None);
        var issued = await store.IssueAsync(userId, null, "este aparelho", CancellationToken.None);

        await store.RevokeSessionAsync(issued.SessionId, RefreshTokenRevocationReason.Logout, CancellationToken.None);

        var revoked = await LoadAsync(issued.Token);
        revoked.RevokedReason.ShouldBe(RefreshTokenRevocationReason.Logout);
        (await store.RotateAsync(issued.Token, CancellationToken.None)).Status.ShouldBe(RefreshRotationStatus.Revoked);
        (await LoadAsync(outra.Token)).RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Emitir_na_sessao_existente_mantem_a_familia()
    {
        var userId = await SeedUserAsync();
        var (store, db, _) = NewStore();
        await using var _db = db;
        var first = await store.IssueAsync(userId, null, null, CancellationToken.None);

        var again = await store.IssueAsync(userId, first.SessionId, null, CancellationToken.None);

        again.SessionId.ShouldBe(first.SessionId);
        again.Token.ShouldNotBe(first.Token);
    }
}
