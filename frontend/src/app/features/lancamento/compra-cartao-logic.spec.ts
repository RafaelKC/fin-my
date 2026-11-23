import { EntradaPrevia, parseValorCartao, previaCompra } from './compra-cartao-logic';

const base: EntradaPrevia = {
  date: '2026-03-17',
  closingDay: 18,
  dueDay: 25,
  cents: 1295,
  parcelas: 1,
  modo: 'total_amount',
  emAndamento: false,
  proxima: 1,
  faturaEmAndamento: '2026-03',
};

describe('previaCompra', () => {
  it('à vista: fatura e vencimento (US2.1)', () => {
    expect(previaCompra(base)).toBe('Fatura de março — vence 25/03');
  });

  it.each([
    ['2026-03-18', 'Fatura de abril — vence 25/04'],
    ['2026-12-31', 'Fatura de janeiro de 2027 — vence 25/01/2027'],
  ])('regra de fatura: compra em %s', (date, esperado) => {
    expect(previaCompra({ ...base, date })).toBe(esperado);
  });

  it('cartão com fechamento no mês anterior ao vencimento (Cartão X)', () => {
    const x = { ...base, closingDay: 28, dueDay: 5 };
    expect(previaCompra({ ...x, date: '2026-02-27' })).toBe('Fatura de março — vence 05/03');
    expect(previaCompra({ ...x, date: '2026-02-28' })).toBe('Fatura de abril — vence 05/04');
  });

  it('à vista não depende do valor', () => {
    expect(previaCompra({ ...base, cents: null })).toBe('Fatura de março — vence 25/03');
  });

  it('data inválida não tem pré-visualização', () => {
    expect(previaCompra({ ...base, date: '' })).toBeNull();
  });

  it('parcelado em valor total: 1ª parcela com a sobra (US3.3)', () => {
    expect(previaCompra({ ...base, date: '2026-01-28', cents: 10000, parcelas: 3 })).toBe(
      '3x de R$ 33,34 (1ª) e R$ 33,33 — primeira fatura: fevereiro',
    );
  });

  it('parcelado sem sobra: parcelas iguais', () => {
    expect(previaCompra({ ...base, cents: 9000, parcelas: 3 })).toBe(
      '3x de R$ 30,00 — primeira fatura: março',
    );
  });

  it('parcelado em valor da parcela mostra o total (US3.2)', () => {
    expect(previaCompra({ ...base, cents: 10279, parcelas: 8, modo: 'installment_amount' })).toBe(
      '8x de R$ 102,79 (total R$ 822,32) — primeira fatura: março',
    );
  });

  it('parcelado sem valor válido, ou com total menor que N centavos, não tem pré-visualização', () => {
    expect(previaCompra({ ...base, cents: null, parcelas: 3 })).toBeNull();
    expect(previaCompra({ ...base, cents: 2, parcelas: 3 })).toBeNull();
  });

  it('em andamento: parcelas restantes a partir da fatura informada (US4.1)', () => {
    expect(
      previaCompra({
        ...base,
        date: '2026-06-20',
        cents: 10274,
        parcelas: 8,
        emAndamento: true,
        proxima: 5,
        faturaEmAndamento: '2026-11',
      }),
    ).toBe('4 parcelas restantes (5/8 a 8/8) de R$ 102,74 — primeira fatura: novembro');
  });

  it('em andamento na última parcela usa o singular; fora do intervalo não mostra nada (US4.2)', () => {
    const e = {
      ...base,
      cents: 10274,
      parcelas: 8,
      emAndamento: true,
      faturaEmAndamento: '2026-11',
    };
    expect(previaCompra({ ...e, proxima: 8 })).toBe(
      '1 parcela restante (8/8) de R$ 102,74 — primeira fatura: novembro',
    );
    expect(previaCompra({ ...e, proxima: 9 })).toBeNull();
    expect(previaCompra({ ...e, proxima: 0 })).toBeNull();
  });
});

describe('parseValorCartao', () => {
  it('aceita positivo e negativo (estorno), expressões incluídas', () => {
    expect(parseValorCartao('12,95')).toEqual({ ok: true, cents: 1295 });
    expect(parseValorCartao('-99,90')).toEqual({ ok: true, cents: -9990 });
    expect(parseValorCartao('239,8/2')).toEqual({ ok: true, cents: 11990 });
  });

  it('recusa zero e texto inválido', () => {
    expect(parseValorCartao('0')).toEqual({ ok: false, error: 'amount.must_be_positive' });
    expect(parseValorCartao('abc').ok).toBe(false);
  });
});
