using FinMy.Domain.Identity;

namespace FinMy.Domain.Tests.Identity;

public class IdentityEntitiesTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("  Maria@Exemplo.COM ", "maria@exemplo.com")]
    [InlineData("a@b.com", "a@b.com")]
    public void Email_e_normalizado_com_trim_e_minusculas(string email, string esperado)
    {
        User.NormalizeEmail(email).ShouldBe(esperado);
    }

    [Fact]
    public void Usuario_novo_guarda_email_como_digitado_e_normalizado_e_gera_stamp()
    {
        var user = new User("  Maria ", " Maria@Exemplo.com ", "hash", Now);

        user.Id.Version.ShouldBe(7);
        user.Name.ShouldBe("Maria");
        user.Email.ShouldBe("Maria@Exemplo.com");
        user.EmailNormalized.ShouldBe("maria@exemplo.com");
        user.SecurityStamp.ShouldNotBe(Guid.Empty);
        user.CreatedAt.ShouldBe(Now);
        user.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Espaco_novo_nasce_com_onboarding_vazio()
    {
        var tenant = new Tenant(" Finanças de Maria ");

        tenant.Id.Version.ShouldBe(7);
        tenant.Name.ShouldBe("Finanças de Maria");
        tenant.OnboardingSteps.ShouldBeEmpty();
        tenant.OnboardingDismissedAt.ShouldBeNull();
    }

    [Fact]
    public void Participacao_nova_e_dona_do_espaco()
    {
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var member = new TenantMember(tenantId, userId, Now);

        member.TenantId.ShouldBe(tenantId);
        member.UserId.ShouldBe(userId);
        member.Role.ShouldBe(TenantMemberRole.Owner);
        member.JoinedAt.ShouldBe(Now);
    }

    [Fact]
    public void Refresh_token_novo_nasce_ativo_com_ultimo_uso_igual_a_criacao()
    {
        var hash = new byte[32];
        var expires = Now.AddDays(90);

        var token = new RefreshToken(Guid.CreateVersion7(), Guid.CreateVersion7(), hash, "Chrome · Android", Now, expires);

        token.Id.Version.ShouldBe(7);
        token.TokenHash.ShouldBe(hash);
        token.CreatedAt.ShouldBe(Now);
        token.LastUsedAt.ShouldBe(Now);
        token.ExpiresAt.ShouldBe(expires);
        token.RevokedAt.ShouldBeNull();
        token.RevokedReason.ShouldBeNull();
        token.ReplacedById.ShouldBeNull();
    }

    [Fact]
    public void Token_de_redefinicao_novo_nao_foi_usado()
    {
        var token = new PasswordResetToken(Guid.CreateVersion7(), new byte[32], Now, Now.AddHours(1));

        token.Id.Version.ShouldBe(7);
        token.ExpiresAt.ShouldBe(Now.AddHours(1));
        token.UsedAt.ShouldBeNull();
    }

    [Fact]
    public void Controle_de_tentativas_novo_comeca_zerado_e_sem_bloqueio()
    {
        var throttle = new LoginThrottle("maria@exemplo.com", Now);

        throttle.EmailNormalized.ShouldBe("maria@exemplo.com");
        throttle.FailedCount.ShouldBe(0);
        throttle.WindowStartedAt.ShouldBe(Now);
        throttle.LockedUntil.ShouldBeNull();
    }
}
