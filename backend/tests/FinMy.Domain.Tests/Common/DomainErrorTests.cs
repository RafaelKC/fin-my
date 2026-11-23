using FinMy.Domain.Common;

namespace FinMy.Domain.Tests.Common;

public class DomainErrorTests
{
    [Fact]
    public void DomainException_expoe_o_erro_e_usa_a_mensagem()
    {
        var error = new DomainError("email_already_registered", "Este e-mail já tem conta.", DomainErrorKind.Conflict);

        var exception = new DomainException(error);

        exception.Error.ShouldBe(error);
        exception.Message.ShouldBe("Este e-mail já tem conta.");
    }

    [Fact]
    public void DomainException_recusa_erro_nulo()
    {
        Should.Throw<ArgumentNullException>(() => new DomainException(null!));
    }
}
