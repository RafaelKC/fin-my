using System.Globalization;
using FinMy.Domain.Cards;
using FinMy.Domain.Monetary;
using FinMy.Domain.Tests.Money;

namespace FinMy.Domain.Tests.Cards;

public class InvoiceCycleTests
{
    private const string File = "invoice-cycle.json";

    public static TheoryData<string> Ids => TestVectors.Ids(File);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Vetor_compartilhado(string id)
    {
        var vector = TestVectors.Get(File, id);
        var date = DateOnly.Parse(vector.Input.GetProperty("date").GetString()!, CultureInfo.InvariantCulture);
        var closingDay = vector.Input.GetProperty("closingDay").GetInt32();
        var dueDay = vector.Input.GetProperty("dueDay").GetInt32();

        var invoice = InvoiceCycle.InvoiceFor(date, closingDay, dueDay);

        invoice.ToString().ShouldBe(vector.Expected.GetProperty("invoiceMonth").GetString());
        InvoiceCycle.ClosingDate(invoice, closingDay, dueDay).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ShouldBe(vector.Expected.GetProperty("closingDate").GetString());
        InvoiceCycle.DueDate(invoice, dueDay).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ShouldBe(vector.Expected.GetProperty("dueDate").GetString());
        InvoiceCycle.CurrentInvoice(date, closingDay, dueDay).ShouldBe(invoice);
    }

    [Fact]
    public void Fatura_nunca_passa_de_dois_meses_adiante()
    {
        for (var day = 0; day < 730; day++)
        {
            var date = new DateOnly(2027, 1, 1).AddDays(day);
            foreach (var (closing, due) in new[] { (1, 10), (30, 8), (31, 1), (10, 10), (28, 5), (18, 25) })
            {
                var invoice = InvoiceCycle.InvoiceFor(date, closing, due);
                invoice.ShouldBeGreaterThanOrEqualTo(YearMonth.From(date));
                invoice.ShouldBeLessThanOrEqualTo(YearMonth.From(date).AddMonths(2));
                InvoiceCycle.ClosingDate(invoice, closing, due).ShouldBeGreaterThan(date);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Dia_fora_de_1_a_31_lanca(int day)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => InvoiceCycle.ClosingDate(YearMonth.Of(2026, 3), day, 10));
        Should.Throw<ArgumentOutOfRangeException>(() => InvoiceCycle.DueDate(YearMonth.Of(2026, 3), day));
    }
}
