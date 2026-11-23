using System.Globalization;
using System.Text;
using FinMy.Domain.Common;

namespace FinMy.Domain.Monetary;

/// <summary>Códigos estáveis de erro do campo de valor (iguais aos da API e dos vetores compartilhados).</summary>
public static class ValueExpressionErrors
{
    public const string InvalidExpression = "amount.invalid_expression";
    public const string DivisionByZero = "amount.division_by_zero";
    public const string MustBePositive = "amount.must_be_positive";
    public const string TooLarge = "amount.too_large";

    /// <summary>Erro de validação do campo <paramref name="field"/> com a mensagem pt-BR do contrato.</summary>
    public static DomainError ToDomainError(string code, string field = "amount")
    {
        var message = code switch
        {
            InvalidExpression => "Valor inválido. Use números e + − × ÷ ( )",
            DivisionByZero => "Divisão por zero",
            MustBePositive => "Informe um valor maior que zero",
            TooLarge => "O valor máximo é R$ 9.999.999,99",
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Código de valor desconhecido."),
        };

        return new DomainError(
            code,
            message,
            DomainErrorKind.Validation,
            new Dictionary<string, string[]> { [field] = [message] });
    }
}

/// <summary>Resultado de <see cref="ValueExpressionParser.Parse"/>: centavos ou um código de erro.</summary>
public readonly record struct ValueParseResult(long Cents, string? Error)
{
    public bool IsSuccess => Error is null;

    public Money Money => IsSuccess ? new Money(Cents) : throw new InvalidOperationException(Error);

    public static ValueParseResult Success(long cents) => new(cents, null);

    public static ValueParseResult Failure(string error) => new(0, error);
}

/// <summary>
/// Campo de valor com calculadora (RT-05): número ou expressão aritmética → centavos. Usa <c>decimal</c>
/// só aqui dentro e arredonda "meio para cima" uma única vez, no fim (RT-02). Menos unário só é aceito no
/// início da expressão ou logo após <c>(</c>. Um <c>=</c> inicial é ignorado.
/// </summary>
public static class ValueExpressionParser
{
    private const int MaxLength = 200;
    private const int MaxDepth = 32;

    /// <summary>
    /// Avalia <paramref name="text"/>. Por padrão exige resultado &gt; 0 (RT-02); com
    /// <paramref name="allowNonPositive"/> aceita zero e negativo (saldo inicial de carteira, 002 FR-001).
    /// O valor máximo (R$ 9.999.999,99, em módulo) vale sempre.
    /// </summary>
    public static ValueParseResult Parse(string? text, bool allowNonPositive = false)
    {
        var source = text?.Trim() ?? string.Empty;
        if (source.StartsWith('='))
        {
            source = source[1..].Trim();
        }

        if (source.Length is 0 or > MaxLength)
        {
            return Fail(ValueExpressionErrors.InvalidExpression);
        }

        try
        {
            var tokens = Tokenize(source);
            if (tokens is null)
            {
                return Fail(ValueExpressionErrors.InvalidExpression);
            }

            var evaluator = new Evaluator(tokens);
            if (!evaluator.TryEvaluate(out var value))
            {
                return Fail(ValueExpressionErrors.InvalidExpression);
            }

            if (evaluator.DivisionByZero)
            {
                return Fail(ValueExpressionErrors.DivisionByZero);
            }

            var cents = Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero);
            if (Math.Abs(cents) > Money.MaxCents)
            {
                return Fail(ValueExpressionErrors.TooLarge);
            }

            if (cents <= 0 && !allowNonPositive)
            {
                return Fail(ValueExpressionErrors.MustBePositive);
            }

            return ValueParseResult.Success((long)cents);
        }
        catch (OverflowException)
        {
            return Fail(ValueExpressionErrors.TooLarge);
        }
    }

    private static ValueParseResult Fail(string error) => ValueParseResult.Failure(error);

    private enum Kind
    {
        Number,
        Plus,
        Minus,
        Multiply,
        Divide,
        Open,
        Close,
        End,
    }

    private readonly record struct Token(Kind Kind, decimal Value = 0);

    private static List<Token>? Tokenize(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is >= '0' and <= '9')
            {
                var end = i;
                while (end < source.Length && (source[end] is (>= '0' and <= '9') or '.' or ','))
                {
                    end++;
                }

                if (!TryReadNumber(source.AsSpan(i, end - i), out var number))
                {
                    return null;
                }

                tokens.Add(new Token(Kind.Number, number));
                i = end;
                continue;
            }

            var kind = c switch
            {
                '+' => Kind.Plus,
                '-' => Kind.Minus,
                '*' or 'x' => Kind.Multiply,
                '/' or '÷' => Kind.Divide,
                '(' => Kind.Open,
                ')' => Kind.Close,
                _ => (Kind?)null,
            };
            if (kind is null)
            {
                return null;
            }

            tokens.Add(new Token(kind.Value));
            i++;
        }

        tokens.Add(new Token(Kind.End));
        return tokens;
    }

    /// <summary>
    /// Número com separadores (RT-05): vírgula é o decimal; ponto seguido de exatamente 3 dígitos é
    /// milhar (ignorado); ponto seguido de 1 ou 2 dígitos é decimal; qualquer outro uso é inválido.
    /// </summary>
    private static bool TryReadNumber(ReadOnlySpan<char> text, out decimal value)
    {
        value = 0;
        var digits = new StringBuilder(text.Length);
        var hasDecimal = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is >= '0' and <= '9')
            {
                digits.Append(c);
                continue;
            }

            if (hasDecimal)
            {
                return false;
            }

            var run = 0;
            while (i + 1 + run < text.Length && text[i + 1 + run] is >= '0' and <= '9')
            {
                run++;
            }

            if (c == ',')
            {
                if (run == 0)
                {
                    return false;
                }

                hasDecimal = true;
                digits.Append('.');
            }
            else if (run == 3)
            {
                // Milhar: ignorado.
            }
            else if (run is 1 or 2)
            {
                hasDecimal = true;
                digits.Append('.');
            }
            else
            {
                return false;
            }
        }

        if (!decimal.TryParse(digits.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            throw new OverflowException();
        }

        return true;
    }

    private sealed class Evaluator(List<Token> tokens)
    {
        private int _position;
        private int _depth;

        /// <summary>Houve divisão por zero; só vale se a sintaxe estiver correta (erro de sintaxe tem prioridade).</summary>
        public bool DivisionByZero { get; private set; }

        private Token Current => tokens[_position];

        public bool TryEvaluate(out decimal value)
        {
            if (TryExpression(out value) && Current.Kind == Kind.End)
            {
                return true;
            }

            value = 0;
            return false;
        }

        // expressão = [-] termo (('+' | '-') termo)* ; o '-' unário só vale no início ou após '('.
        private bool TryExpression(out decimal value)
        {
            var negative = false;
            if (Current.Kind == Kind.Minus)
            {
                negative = true;
                _position++;
            }

            if (!TryTerm(out value))
            {
                return false;
            }

            if (negative)
            {
                value = -value;
            }

            while (Current.Kind is Kind.Plus or Kind.Minus)
            {
                var plus = Current.Kind == Kind.Plus;
                _position++;
                if (!TryTerm(out var right))
                {
                    return false;
                }

                value = plus ? value + right : value - right;
            }

            return true;
        }

        private bool TryTerm(out decimal value)
        {
            if (!TryFactor(out value))
            {
                return false;
            }

            while (Current.Kind is Kind.Multiply or Kind.Divide)
            {
                var multiply = Current.Kind == Kind.Multiply;
                _position++;
                if (!TryFactor(out var right))
                {
                    return false;
                }

                if (multiply)
                {
                    value *= right;
                }
                else if (right == 0)
                {
                    DivisionByZero = true;
                    value = 0;
                }
                else
                {
                    value /= right;
                }
            }

            return true;
        }

        private bool TryFactor(out decimal value)
        {
            value = 0;
            if (Current.Kind == Kind.Number)
            {
                value = Current.Value;
                _position++;
                return true;
            }

            if (Current.Kind != Kind.Open || _depth >= MaxDepth)
            {
                return false;
            }

            _position++;
            _depth++;
            var ok = TryExpression(out value) && Current.Kind == Kind.Close;
            _depth--;
            if (!ok)
            {
                return false;
            }

            _position++;
            return true;
        }
    }
}
