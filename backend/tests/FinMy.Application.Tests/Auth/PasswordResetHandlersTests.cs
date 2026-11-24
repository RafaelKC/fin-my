using FinMy.Application.Auth;
using FinMy.Application.Tests.Email;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Auth;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Jobs;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinMy.Application.Tests.Auth;

/// <summary>US4 (cenários 4.1–4.4) com banco real e <see cref="FakeEmailSender"/>.</summary>
[Collection(PostgresTestGroup.Name)]
public class PasswordResetHandlersTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly AppUrl App = new("https://app.exemplo.com/");

    private readonly FakeClock _clock = new(Start);
    private readonly FakeEmailSender _emails = new();
    private readonly PasswordHasherAdapter _hasher = new();
    private FinMyDbContext _db = null!;
    private readonly RecordingOAuthRevoker _oauth = new();
    private RefreshTokenStore _sessions = null!;

    public Task InitializeAsync()
    {
        _db = postgres.CreateContext(new CurrentContext(), _clock);
        _sessions = new RefreshTokenStore(_db, _clock, new SessionRegistry(_db, _clock, new MemoryCache(new MemoryCacheOptions())));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private RequestPasswordResetHandler NewRequest()
        => new(new InlinePasswordResetDispatcher(
            new PasswordResetMailer(new PasswordResetStore(_db), _emails, _clock, App),
            NullLogger<InlinePasswordResetDispatcher>.Instance));

    private ConfirmPasswordResetHandler NewConfirm() => new(new PasswordResetStore(_db), _hasher, _sessions, _oauth, _clock);

    private async Task<User> SeedUserAsync(string? email = null)
    {
        var user = new User("Maria Souza", email ?? $"maria-{Guid.NewGuid():N}@example.com", _hasher.Hash("senha-antiga-123"), Start);
        _db.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private static string TokenFrom(string text)
    {
        var start = text.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var end = text.IndexOfAny(['\r', '\n', ' ', '"'], start);
        return text[start..(end < 0 ? text.Length : end)];
    }

    private async Task<string> RequestTokenAsync(User user)
    {
        await NewRequest().HandleAsync(new RequestPasswordResetCommand(user.Email), CancellationToken.None);
        return TokenFrom(_emails.Sent[^1].TextBody);
    }

    private async Task<User> ReloadAsync(Guid id)
    {
        await using var db = postgres.CreateContext(new CurrentContext());
        return await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private static async Task<DomainException> ShouldFailAsync(Task action)
        => await Should.ThrowAsync<DomainException>(async () => await action);

    [Fact]
    public async Task Cenario_4_1_conta_existente_recebe_e_mail_pt_BR_com_link_de_1_hora_e_so_o_hash_fica_no_banco()
    {
        var user = await SeedUserAsync();

        await NewRequest().HandleAsync(new RequestPasswordResetCommand($"  {user.Email.ToUpperInvariant()} "), CancellationToken.None);

        var mail = _emails.Sent.ShouldHaveSingleItem();
        mail.To.ShouldBe(user.Email);
        mail.Subject.ShouldBe("Redefinição de senha do Fin-My");
        mail.TextBody.ShouldContain("https://app.exemplo.com/redefinir-senha?token=");
        mail.HtmlBody!.ShouldContain("https://app.exemplo.com/redefinir-senha?token=");

        var token = TokenFrom(mail.TextBody);
        token.Length.ShouldBe(43);

        var saved = await _db.Set<PasswordResetToken>().AsNoTracking().SingleAsync(t => t.UserId == user.Id);
        saved.TokenHash.ShouldBe(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        saved.ExpiresAt.ShouldBe(Start.AddHours(1));
        saved.UsedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Cenario_4_2_conta_inexistente_termina_igual_sem_e_mail_e_sem_token()
    {
        var before = await _db.Set<PasswordResetToken>().CountAsync();

        await NewRequest().HandleAsync(new RequestPasswordResetCommand("ninguem@example.com"), CancellationToken.None);

        _emails.Sent.ShouldBeEmpty();
        (await _db.Set<PasswordResetToken>().CountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task Falha_de_SMTP_nao_aparece_no_pedido_nem_muda_o_resultado()
    {
        var user = await SeedUserAsync();
        _emails.Failure = new IOException("SMTP fora do ar");

        await Should.NotThrowAsync(() => NewRequest().HandleAsync(new RequestPasswordResetCommand(user.Email), CancellationToken.None));

        _emails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Job_do_Hangfire_propaga_a_falha_de_SMTP_para_o_Hangfire_tentar_de_novo()
    {
        var user = await SeedUserAsync();
        _emails.Failure = new IOException("SMTP fora do ar");
        var job = new PasswordResetEmailJob(new PasswordResetMailer(new PasswordResetStore(_db), _emails, _clock, App));

        await Should.ThrowAsync<IOException>(() => job.RunAsync(user.Email));

        _emails.Failure = null;
        await job.RunAsync(user.Email);
        _emails.Sent.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Pedido_sem_e_mail_e_422(string? email)
    {
        var error = await ShouldFailAsync(NewRequest().HandleAsync(new RequestPasswordResetCommand(email), CancellationToken.None));

        error.Error.Kind.ShouldBe(DomainErrorKind.Validation);
        error.Error.FieldErrors!.ShouldContainKey("email");
    }

    [Fact]
    public async Task Pedir_de_novo_nao_invalida_o_link_anterior()
    {
        var user = await SeedUserAsync();
        var first = await RequestTokenAsync(user);
        var second = await RequestTokenAsync(user);

        first.ShouldNotBe(second);
        await NewConfirm().HandleAsync(new ConfirmPasswordResetCommand(first, "nova-senha-456"), CancellationToken.None);
    }

    [Fact]
    public async Task Cenario_4_3_confirmar_troca_a_senha_renova_o_stamp_marca_o_token_e_encerra_todas_as_sessoes()
    {
        var user = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var phone = await _sessions.IssueAsync(user.Id, null, "Celular", CancellationToken.None);
        var laptop = await _sessions.IssueAsync(user.Id, null, "Notebook", CancellationToken.None);
        var others = await _sessions.IssueAsync(otherUser.Id, null, null, CancellationToken.None);
        var token = await RequestTokenAsync(user);
        var oldStamp = user.SecurityStamp;

        await NewConfirm().HandleAsync(new ConfirmPasswordResetCommand(token, "nova-senha-456"), CancellationToken.None);

        var reloaded = await ReloadAsync(user.Id);
        _hasher.Verify(reloaded.PasswordHash, "nova-senha-456").ShouldBe(Application.Abstractions.PasswordCheck.Success);
        _hasher.Verify(reloaded.PasswordHash, "senha-antiga-123").ShouldBe(Application.Abstractions.PasswordCheck.Failed);
        reloaded.SecurityStamp.ShouldNotBe(oldStamp);

        var used = await _db.Set<PasswordResetToken>().AsNoTracking().SingleAsync(t => t.UserId == user.Id);
        used.UsedAt.ShouldBe(Start);

        var userTokens = await _db.Set<RefreshToken>().AsNoTracking().Where(r => r.UserId == user.Id).ToListAsync();
        userTokens.Count.ShouldBe(2);
        userTokens.ShouldAllBe(r => r.RevokedAt != null && r.RevokedReason == RefreshTokenRevocationReason.PasswordReset);

        // Quem não pediu a redefinição continua logado.
        var untouched = await _db.Set<RefreshToken>().AsNoTracking().SingleAsync(r => r.UserId == otherUser.Id);
        untouched.RevokedAt.ShouldBeNull();

        (await _sessions.RotateAsync(phone.Token, CancellationToken.None)).Status.ShouldBe(Application.Abstractions.RefreshRotationStatus.Revoked);
        (await _sessions.RotateAsync(laptop.Token, CancellationToken.None)).Status.ShouldBe(Application.Abstractions.RefreshRotationStatus.Revoked);
        (await _sessions.RotateAsync(others.Token, CancellationToken.None)).Status.ShouldBe(Application.Abstractions.RefreshRotationStatus.Rotated);

        // Os conectores (OAuth, 008) também precisam ser autorizados de novo, só os de quem redefiniu.
        _oauth.Revoked.ShouldBe([user.Id]);
    }

    [Fact]
    public async Task Cenario_4_4_link_usado_de_novo_e_410_reset_token_invalid()
    {
        var user = await SeedUserAsync();
        var token = await RequestTokenAsync(user);
        await NewConfirm().HandleAsync(new ConfirmPasswordResetCommand(token, "nova-senha-456"), CancellationToken.None);

        var error = await ShouldFailAsync(NewConfirm().HandleAsync(new ConfirmPasswordResetCommand(token, "outra-senha-789"), CancellationToken.None));

        error.Error.Code.ShouldBe("reset_token_invalid");
        error.Error.Kind.ShouldBe(DomainErrorKind.Gone);
        var reloaded = await ReloadAsync(user.Id);
        _hasher.Verify(reloaded.PasswordHash, "nova-senha-456").ShouldBe(Application.Abstractions.PasswordCheck.Success);
    }

    [Fact]
    public async Task Cenario_4_4_link_com_mais_de_1_hora_e_410_e_nada_muda()
    {
        var user = await SeedUserAsync();
        var token = await RequestTokenAsync(user);
        var oldStamp = user.SecurityStamp;
        await _sessions.IssueAsync(user.Id, null, null, CancellationToken.None);

        _clock.UtcNow = Start.AddHours(1).AddSeconds(1);
        var error = await ShouldFailAsync(NewConfirm().HandleAsync(new ConfirmPasswordResetCommand(token, "nova-senha-456"), CancellationToken.None));

        error.Error.Code.ShouldBe("reset_token_invalid");
        var reloaded = await ReloadAsync(user.Id);
        reloaded.SecurityStamp.ShouldBe(oldStamp);
        (await _db.Set<RefreshToken>().AsNoTracking().SingleAsync(r => r.UserId == user.Id)).RevokedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("token-que-nunca-existiu")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Cenario_4_4_token_inexistente_ou_vazio_e_410(string? token)
    {
        var error = await ShouldFailAsync(NewConfirm().HandleAsync(new ConfirmPasswordResetCommand(token, "nova-senha-456"), CancellationToken.None));

        error.Error.Code.ShouldBe("reset_token_invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("curta")]
    public async Task Senha_curta_ou_ausente_e_422_e_nao_gasta_o_token(string? password)
    {
        var user = await SeedUserAsync();
        var token = await RequestTokenAsync(user);

        var error = await ShouldFailAsync(NewConfirm().HandleAsync(new ConfirmPasswordResetCommand(token, password), CancellationToken.None));

        error.Error.Kind.ShouldBe(DomainErrorKind.Validation);
        error.Error.FieldErrors!.ShouldContainKey("newPassword");
        await NewConfirm().HandleAsync(new ConfirmPasswordResetCommand(token, "nova-senha-456"), CancellationToken.None);
    }

    [Fact]
    public async Task Senha_com_mais_de_128_caracteres_e_422()
    {
        var error = await ShouldFailAsync(NewConfirm().HandleAsync(new ConfirmPasswordResetCommand("x", new string('a', 129)), CancellationToken.None));

        error.Error.Kind.ShouldBe(DomainErrorKind.Validation);
    }
}
