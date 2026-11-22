using FinMy.Domain.Common;

namespace FinMy.Domain.Tests.Common;

public class ChannelTests
{
    [Fact]
    public void Tem_exatamente_app_claude_e_system()
    {
        Enum.GetNames<Channel>().Order().ShouldBe(["App", "Claude", "System"]);
    }
}
