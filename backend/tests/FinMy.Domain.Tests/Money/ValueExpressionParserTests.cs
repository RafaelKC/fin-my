using FinMy.Domain.Monetary;

namespace FinMy.Domain.Tests.Money;

public class ValueExpressionParserTests
{
    private const string File = "value-expression.json";

    public static TheoryData<string> Ids => TestVectors.Ids(File);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Vetor_compartilhado(string id)
    {
        var vector = TestVectors.Get(File, id);

        var result = ValueExpressionParser.Parse(vector.Input.GetProperty("text").GetString());

        if (vector.Expected.TryGetProperty("error", out var error))
        {
            result.IsSuccess.ShouldBeFalse();
            result.Error.ShouldBe(error.GetString());
        }
        else
        {
            result.Error.ShouldBeNull();
            result.Cents.ShouldBe(vector.Expected.GetProperty("amountCents").GetInt64());
        }
    }

    [Fact]
    public void Texto_nulo_e_invalido()
    {
        ValueExpressionParser.Parse(null).Error.ShouldBe(ValueExpressionErrors.InvalidExpression);
    }

    [Fact]
    public void Expressao_muito_longa_e_invalida()
    {
        ValueExpressionParser.Parse(new string('1', 201)).Error.ShouldBe(ValueExpressionErrors.InvalidExpression);
    }

    [Fact]
    public void Parenteses_muito_aninhados_sao_invalidos()
    {
        var text = new string('(', 100) + "1" + new string(')', 100);

        ValueExpressionParser.Parse(text).Error.ShouldBe(ValueExpressionErrors.InvalidExpression);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-5", -500)]
    [InlineData("10-10", 0)]
    [InlineData("-1.234,56", -123456)]
    [InlineData("-(2+3)", -500)]
    [InlineData("44,99", 4499)]
    public void Saldo_inicial_aceita_zero_e_negativo(string text, long cents)
    {
        var result = ValueExpressionParser.Parse(text, allowNonPositive: true);

        result.IsSuccess.ShouldBeTrue();
        result.Cents.ShouldBe(cents);
    }

    [Theory]
    [InlineData("10000000")]
    [InlineData("-10000000")]
    public void Valor_maximo_vale_tambem_com_sinal_permitido(string text)
    {
        ValueExpressionParser.Parse(text, allowNonPositive: true).Error.ShouldBe(ValueExpressionErrors.TooLarge);
    }

    [Fact]
    public void Resultado_de_sucesso_expoe_Money()
    {
        ValueExpressionParser.Parse("29+73,2").Money.ShouldBe(Monetary.Money.FromCents(10220));
    }

    [Fact]
    public void Money_de_falha_lanca()
    {
        Should.Throw<InvalidOperationException>(() => ValueExpressionParser.Parse("x").Money);
    }

    [Theory]
    [InlineData(ValueExpressionErrors.InvalidExpression, "Valor inválido. Use números e + − × ÷ ( )")]
    [InlineData(ValueExpressionErrors.DivisionByZero, "Divisão por zero")]
    [InlineData(ValueExpressionErrors.MustBePositive, "Informe um valor maior que zero")]
    [InlineData(ValueExpressionErrors.TooLarge, "O valor máximo é R$ 9.999.999,99")]
    public void Erro_de_dominio_tem_codigo_e_mensagem_pt_br(string code, string message)
    {
        var error = ValueExpressionErrors.ToDomainError(code);

        error.Code.ShouldBe(code);
        error.Message.ShouldBe(message);
        error.FieldErrors!["amount"].ShouldBe([message]);
    }

    [Fact]
    public void Codigo_desconhecido_lanca()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ValueExpressionErrors.ToDomainError("outro"));
    }
}
