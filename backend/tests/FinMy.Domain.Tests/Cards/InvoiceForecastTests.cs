using System.Globalization;
using System.Text.Json;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Tests.Money;
using Dinheiro = FinMy.Domain.Monetary.Money;

namespace FinMy.Domain.Tests.Cards;

public class InvoiceForecastTests
{
    private const string File = "invoice-forecast.json";

    public static TheoryData<string> Ids => TestVectors.Ids(File);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Vetor_compartilhado(string id)
    {
        var vector = TestVectors.Get(File, id);
        var input = vector.Input;
        var estimateElement = input.GetProperty("estimateCents");
        long? estimate = estimateElement.ValueKind == JsonValueKind.Null ? null : estimateElement.GetInt64();
        var today = DateOnly.Parse(input.GetProperty("today").GetString()!, CultureInfo.InvariantCulture);
        var closing = DateOnly.Parse(input.GetProperty("closingDate").GetString()!, CultureInfo.InvariantCulture);

        var planned = InvoiceForecast.Planned(
            input.GetProperty("totalCents").GetInt64(), input.GetProperty("paidCents").GetInt64(), estimate, today, closing);

        planned.ShouldBe(vector.Expected.GetProperty("plannedCents").GetInt64());
        InvoiceForecast.EstimateActive(estimate, today, closing).ShouldBe(vector.Expected.GetProperty("estimateActive").GetBoolean());
    }

    private static Invoice NewInvoice() =>
        Invoice.Create(Guid.CreateVersion7(), YearMonth.Of(2027, 5), new DateOnly(2027, 5, 18), new DateOnly(2027, 5, 25));

    [Fact]
    public void Fatura_nasce_sem_estimativa_e_aceita_definir_e_remover()
    {
        var invoice = NewInvoice();
        invoice.EstimateCents.ShouldBeNull();

        invoice.SetEstimate(Dinheiro.FromCents(80000));
        invoice.EstimateCents.ShouldBe(80000);

        invoice.SetEstimate(null);
        invoice.EstimateCents.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(1_000_000_000)]
    public void Estimativa_fora_de_1_ate_o_maximo_e_recusada(long cents)
    {
        var invoice = NewInvoice();

        Should.Throw<DomainException>(() => invoice.SetEstimate(Dinheiro.FromCents(cents)));
        invoice.EstimateCents.ShouldBeNull();
    }
}
