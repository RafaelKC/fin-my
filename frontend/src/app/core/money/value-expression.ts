/** Códigos estáveis de erro do campo de valor (iguais aos da API e dos vetores compartilhados). */
export const ValueExpressionErrors = {
  invalidExpression: 'amount.invalid_expression',
  divisionByZero: 'amount.division_by_zero',
  mustBePositive: 'amount.must_be_positive',
  tooLarge: 'amount.too_large',
} as const;

export type ValueExpressionError =
  (typeof ValueExpressionErrors)[keyof typeof ValueExpressionErrors];

export type ValueParseResult =
  | { readonly ok: true; readonly cents: number }
  | { readonly ok: false; readonly error: ValueExpressionError };

/** Mensagens pt-BR do contrato para cada código de erro. */
export const valueExpressionMessages: Readonly<Record<ValueExpressionError, string>> = {
  'amount.invalid_expression': 'Valor inválido. Use números e + − × ÷ ( )',
  'amount.division_by_zero': 'Divisão por zero',
  'amount.must_be_positive': 'Informe um valor maior que zero',
  'amount.too_large': 'O valor máximo é R$ 9.999.999,99',
};

/** Maior valor aceito: R$ 9.999.999,99 (em módulo). */
export const MAX_CENTS = 999_999_999;

const MAX_LENGTH = 200;
const MAX_DEPTH = 32;
/** Maior inteiro de um `decimal` do .NET; acima disso o backend responde `too_large`. */
const DECIMAL_MAX = 79228162514264337593543950335n;

/** Fração exata (denominador sempre positivo): evita os erros de ponto flutuante. */
interface Fraction {
  readonly n: bigint;
  readonly d: bigint;
}

type Token =
  | { readonly kind: 'number'; readonly value: Fraction }
  | { readonly kind: 'plus' | 'minus' | 'multiply' | 'divide' | 'open' | 'close' | 'end' };

const SYMBOLS: Readonly<Record<string, Token['kind']>> = {
  '+': 'plus',
  '-': 'minus',
  '*': 'multiply',
  x: 'multiply',
  '/': 'divide',
  '÷': 'divide',
  '(': 'open',
  ')': 'close',
};

class TooLargeError extends Error {}

const isDigit = (c: string): boolean => c >= '0' && c <= '9';

/**
 * Campo de valor com calculadora (RT-05): número ou expressão aritmética → centavos. Porta TypeScript
 * de `ValueExpressionParser` (backend); as mesmas regras e os mesmos vetores de `shared/test-vectors`.
 * Usa frações exatas e arredonda "meio para cima" uma única vez, no fim (RT-02). Menos unário só no
 * início ou logo após `(`; um `=` inicial é ignorado.
 *
 * Por padrão exige resultado > 0; com `allowNonPositive` aceita zero e negativo (saldo inicial).
 */
export function parseValueExpression(
  text: string | null | undefined,
  allowNonPositive = false,
): ValueParseResult {
  let source = (text ?? '').trim();
  if (source.startsWith('=')) {
    source = source.slice(1).trim();
  }

  if (source.length === 0 || source.length > MAX_LENGTH) {
    return fail(ValueExpressionErrors.invalidExpression);
  }

  try {
    const tokens = tokenize(source);
    if (tokens === null) {
      return fail(ValueExpressionErrors.invalidExpression);
    }

    const evaluator = new Evaluator(tokens);
    const value = evaluator.evaluate();
    if (value === null) {
      return fail(ValueExpressionErrors.invalidExpression);
    }

    if (evaluator.divisionByZero) {
      return fail(ValueExpressionErrors.divisionByZero);
    }

    const cents = roundHalfAwayFromZero({ n: value.n * 100n, d: value.d });
    const absolute = cents < 0n ? -cents : cents;
    if (absolute > BigInt(MAX_CENTS)) {
      return fail(ValueExpressionErrors.tooLarge);
    }

    if (cents <= 0n && !allowNonPositive) {
      return fail(ValueExpressionErrors.mustBePositive);
    }

    return { ok: true, cents: Number(cents) };
  } catch (e) {
    if (e instanceof TooLargeError) {
      return fail(ValueExpressionErrors.tooLarge);
    }
    throw e;
  }
}

function fail(error: ValueExpressionError): ValueParseResult {
  return { ok: false, error };
}

function roundHalfAwayFromZero(value: Fraction): bigint {
  const negative = value.n < 0n;
  const absolute = negative ? -value.n : value.n;
  const rounded = (2n * absolute + value.d) / (2n * value.d);
  return negative ? -rounded : rounded;
}

function tokenize(source: string): Token[] | null {
  const tokens: Token[] = [];
  let i = 0;
  while (i < source.length) {
    const c = source[i];
    if (/\s/u.test(c)) {
      i++;
      continue;
    }

    if (isDigit(c)) {
      let end = i;
      while (
        end < source.length &&
        (isDigit(source[end]) || source[end] === '.' || source[end] === ',')
      ) {
        end++;
      }

      const value = readNumber(source.slice(i, end));
      if (value === null) {
        return null;
      }

      tokens.push({ kind: 'number', value });
      i = end;
      continue;
    }

    const kind = SYMBOLS[c];
    if (kind === undefined) {
      return null;
    }

    tokens.push({ kind } as Token);
    i++;
  }

  tokens.push({ kind: 'end' });
  return tokens;
}

/**
 * Número com separadores (RT-05): vírgula é o decimal; ponto seguido de exatamente 3 dígitos é milhar
 * (ignorado); ponto seguido de 1 ou 2 dígitos é decimal; qualquer outro uso é inválido.
 */
function readNumber(text: string): Fraction | null {
  let digits = '';
  let decimals = 0;
  let hasDecimal = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (isDigit(c)) {
      digits += c;
      if (hasDecimal) {
        decimals++;
      }
      continue;
    }

    if (hasDecimal) {
      return null;
    }

    let run = 0;
    while (i + 1 + run < text.length && isDigit(text[i + 1 + run])) {
      run++;
    }

    if (c === ',') {
      if (run === 0) {
        return null;
      }
      hasDecimal = true;
    } else if (run === 3) {
      // Milhar: ignorado.
    } else if (run === 1 || run === 2) {
      hasDecimal = true;
    } else {
      return null;
    }
  }

  const integerPart = BigInt(digits.slice(0, digits.length - decimals) || '0');
  if (integerPart > DECIMAL_MAX) {
    throw new TooLargeError();
  }

  return { n: BigInt(digits), d: 10n ** BigInt(decimals) };
}

class Evaluator {
  private position = 0;
  private depth = 0;

  /** Houve divisão por zero; só vale se a sintaxe estiver correta (erro de sintaxe tem prioridade). */
  divisionByZero = false;

  constructor(private readonly tokens: readonly Token[]) {}

  private get current(): Token {
    return this.tokens[this.position];
  }

  evaluate(): Fraction | null {
    const value = this.expression();
    return value !== null && this.current.kind === 'end' ? value : null;
  }

  // expressão = [-] termo (('+' | '-') termo)* ; o '-' unário só vale no início ou após '('.
  private expression(): Fraction | null {
    let negative = false;
    if (this.current.kind === 'minus') {
      negative = true;
      this.position++;
    }

    let value: Fraction | null = this.term();
    if (value === null) {
      return null;
    }

    if (negative) {
      value = { n: -value.n, d: value.d };
    }

    while (this.current.kind === 'plus' || this.current.kind === 'minus') {
      const plus = this.current.kind === 'plus';
      this.position++;
      const right = this.term();
      if (right === null) {
        return null;
      }

      const n: bigint = value.n * right.d + (plus ? right.n : -right.n) * value.d;
      value = { n, d: value.d * right.d };
    }

    return value;
  }

  private term(): Fraction | null {
    let value: Fraction | null = this.factor();
    if (value === null) {
      return null;
    }

    while (this.current.kind === 'multiply' || this.current.kind === 'divide') {
      const multiply = this.current.kind === 'multiply';
      this.position++;
      const right = this.factor();
      if (right === null) {
        return null;
      }

      if (multiply) {
        value = { n: value.n * right.n, d: value.d * right.d };
      } else if (right.n === 0n) {
        this.divisionByZero = true;
        value = { n: 0n, d: 1n };
      } else {
        const sign = right.n < 0n ? -1n : 1n;
        value = { n: sign * value.n * right.d, d: sign * value.d * right.n };
      }
    }

    return value;
  }

  private factor(): Fraction | null {
    const token = this.current;
    if (token.kind === 'number') {
      this.position++;
      return token.value;
    }

    if (token.kind !== 'open' || this.depth >= MAX_DEPTH) {
      return null;
    }

    this.position++;
    this.depth++;
    const value = this.expression();
    const closed = value !== null && this.current.kind === 'close';
    this.depth--;
    if (!closed) {
      return null;
    }

    this.position++;
    return value;
  }
}
