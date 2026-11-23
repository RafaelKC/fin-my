using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Transfers;
using Dinheiro = FinMy.Domain.Monetary.Money;

namespace FinMy.Domain.Tests.Transfers;

public class TransferRulesTests
{
    private static readonly Guid Azul = Guid.CreateVersion7();
    private static readonly Guid Verde = Guid.CreateVersion7();
    private static readonly DateOnly Data = new(2026, 3, 10);

    [Fact]
    public void Descricao_padrao_e_Transferencia()
    {
        var transfer = Transfer.Create(Azul, Verde, Dinheiro.FromCents(10_000), Data, null, null);

        transfer.Description.ShouldBe("Transferência");
        transfer.DescriptionNormalized.ShouldBe("transferencia");
        transfer.AmountCents.ShouldBe(10_000);
        transfer.TransferDate.ShouldBe(Data);
        transfer.Notes.ShouldBeNull();
    }

    [Fact]
    public void Descricao_informada_e_normalizada()
    {
        var transfer = Transfer.Create(Azul, Verde, Dinheiro.FromCents(1), Data, "  Reserva Lia ", "  ");

        transfer.Description.ShouldBe("Reserva Lia");
        transfer.DescriptionNormalized.ShouldBe("reserva lia");
        transfer.Notes.ShouldBeNull();
    }

    [Fact]
    public void Origem_e_destino_precisam_ser_diferentes()
    {
        Should.Throw<DomainException>(() => Transfer.Create(Azul, Azul, Dinheiro.FromCents(1), Data, null, null))
            .Error.Code.ShouldBe("transfer.same_wallet");
    }

    [Theory]
    [InlineData(0, "amount.must_be_positive")]
    [InlineData(-1, "amount.must_be_positive")]
    [InlineData(1_000_000_000, "amount.too_large")]
    public void Valor_fora_do_intervalo_e_recusado(long cents, string code)
    {
        Should.Throw<DomainException>(() => Transfer.Create(Azul, Verde, Dinheiro.FromCents(cents), Data, null, null))
            .Error.Code.ShouldBe(code);
    }

    [Fact]
    public void Data_fora_do_intervalo_e_descricao_longa_sao_recusadas()
    {
        Should.Throw<DomainException>(() => Transfer.Create(Azul, Verde, Dinheiro.FromCents(1), new DateOnly(2101, 1, 1), null, null))
            .Error.Code.ShouldBe("date.out_of_range");
        Should.Throw<DomainException>(() => Transfer.Create(Azul, Verde, Dinheiro.FromCents(1), Data, new string('a', 81), null))
            .Error.Code.ShouldBe("description.too_long");
    }
}
