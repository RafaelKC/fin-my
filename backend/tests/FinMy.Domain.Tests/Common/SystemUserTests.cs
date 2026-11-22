using FinMy.Domain.Common;

namespace FinMy.Domain.Tests.Common;

public class SystemUserTests
{
    [Fact]
    public void Id_e_fixo()
    {
        SystemUser.Id.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    }
}
