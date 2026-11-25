using FinMy.Domain.Common;

namespace FinMy.Domain.Tests.Common;

public class ChannelTests
{
    [Fact]
    public void Tem_exatamente_app_claude_system_e_android_widget()
    {
        Enum.GetNames<Channel>().Order().ShouldBe(["AndroidWidget", "App", "Claude", "System"]);
    }

    [Theory]
    [InlineData(Channel.App, "app")]
    [InlineData(Channel.Claude, "claude")]
    [InlineData(Channel.System, "system")]
    [InlineData(Channel.AndroidWidget, "android_widget")]
    public void Texto_do_canal_e_estavel_e_volta_ao_mesmo_canal(Channel channel, string code)
    {
        channel.ToCode().ShouldBe(code);
        ChannelCodes.TryParse(code, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(channel);
        ChannelCodes.Parse(code.ToUpperInvariant()).ShouldBe(channel);
    }

    [Theory]
    [InlineData("androidwidget")]
    [InlineData("web")]
    [InlineData("")]
    [InlineData(null)]
    public void Texto_desconhecido_nao_e_canal(string? code)
    {
        ChannelCodes.TryParse(code, out _).ShouldBeFalse();
    }
}
