using System.Globalization;
using FinMy.Domain.Cards;
using FinMy.Domain.Tests.Money;

namespace FinMy.Domain.Tests.Cards;

public class InvoiceStatusRuleTests
{
    private const string File = "invoice-status.json";

    public static TheoryData<string> Ids => TestVectors.Ids(File);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Vetor_compartilhado(string id)
    {
        var vector = TestVectors.Get(File, id);
        var input = vector.Input;

        var status = InvoiceStatusRule.Evaluate(
            input.GetProperty("totalCents").GetInt64(),
            input.GetProperty("paidCents").GetInt64(),
            DateOnly.Parse(input.GetProperty("today").GetString()!, CultureInfo.InvariantCulture),
            DateOnly.Parse(input.GetProperty("closingDate").GetString()!, CultureInfo.InvariantCulture),
            DateOnly.Parse(input.GetProperty("dueDate").GetString()!, CultureInfo.InvariantCulture));

        status.ToCode().ShouldBe(vector.Expected.GetProperty("status").GetString());
    }
}
