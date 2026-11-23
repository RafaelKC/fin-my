using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FinMy.Application.Tests.Auth;

public class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly JwtOptions Options = new() { SigningKey = "chave-de-teste-chave-de-teste-chave-de-teste-0123456789" };

    private static readonly AccessTokenRequest Request = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Maria Teste", Channel.App);

    private static JwtTokenService Service(DateTimeOffset now, JwtOptions? options = null)
        => new(Microsoft.Extensions.Options.Options.Create(options ?? Options), new FakeClock(now));

    [Fact]
    public async Task Token_leva_as_claims_e_expira_em_15_minutos()
    {
        var issued = Service(DateTimeOffset.UtcNow).IssueAccessToken(Request);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.Token, Options.ToValidationParameters());

        result.IsValid.ShouldBeTrue();
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.GetClaim(TokenClaims.Subject).Value.ShouldBe(Request.UserId.ToString());
        jwt.GetClaim(TokenClaims.Tenant).Value.ShouldBe(Request.TenantId.ToString());
        jwt.GetClaim(TokenClaims.Session).Value.ShouldBe(Request.SessionId.ToString());
        jwt.GetClaim(TokenClaims.Name).Value.ShouldBe("Maria Teste");
        jwt.GetClaim(TokenClaims.Channel).Value.ShouldBe("app");
        (jwt.ValidTo - jwt.ValidFrom).ShouldBe(TimeSpan.FromMinutes(15));
        new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero).ShouldBe(issued.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Expiracao_informada_e_agora_mais_15_minutos()
    {
        Service(Now).IssueAccessToken(Request).ExpiresAt.ShouldBe(Now.AddMinutes(15));
    }

    [Fact]
    public void Canal_claude_vai_em_minusculas()
    {
        var issued = Service(Now).IssueAccessToken(Request with { Channel = Channel.Claude });

        new JsonWebToken(issued.Token).GetClaim(TokenClaims.Channel).Value.ShouldBe("claude");
    }

    [Fact]
    public async Task Token_vencido_e_rejeitado()
    {
        var issued = Service(DateTimeOffset.UtcNow.AddMinutes(-16)).IssueAccessToken(Request);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.Token, Options.ToValidationParameters());

        result.IsValid.ShouldBeFalse();
        result.Exception.ShouldBeOfType<SecurityTokenExpiredException>();
    }

    [Fact]
    public async Task Token_assinado_com_outra_chave_e_rejeitado()
    {
        var outra = new JwtOptions { SigningKey = "outra-chave-outra-chave-outra-chave-outra-chave-0123" };
        var issued = Service(DateTimeOffset.UtcNow, outra).IssueAccessToken(Request);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.Token, Options.ToValidationParameters());

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task Token_sem_assinatura_alg_none_e_rejeitado()
    {
        var unsigned = new JsonWebTokenHandler().CreateToken(
            $$"""{"sub":"{{Request.UserId}}","exp":{{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}},"iss":"fin-my","aud":"fin-my"}""");

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(unsigned, Options.ToValidationParameters());

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("curta")]
    public void Chave_curta_ou_ausente_e_recusada_na_validacao_das_opcoes(string key)
    {
        new JwtOptionsValidator().Validate(null, new JwtOptions { SigningKey = key }).Failed.ShouldBeTrue();
        new JwtOptionsValidator().Validate(null, Options).Succeeded.ShouldBeTrue();
    }
}
