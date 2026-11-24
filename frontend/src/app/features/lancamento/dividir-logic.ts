import { formatBrl, parseValueExpression, splitMoney } from '../../core/money';
import { SplitInput } from './entry.models';

export const PARTES_MIN = 2;
export const PARTES_MAX = 10;

export type ModoDivisao = 'equal' | 'amounts';

/** Uma parte no formulário: `tagId` `null` = "Eu (sem tag de acerto)"; `valor` só em `amounts`. */
export interface ParteDivisao {
  tagId: string | null;
  valor: string;
}

export const ROTULO_EU = 'Eu (sem tag de acerto)';

/** Partes iniciais: "Eu" e uma parte ainda sem tag escolhida (a pessoa escolhe a de acerto). */
export function partesIniciais(tagDeAcerto: string | null): ParteDivisao[] {
  return [
    { tagId: null, valor: '' },
    { tagId: tagDeAcerto, valor: '' },
  ];
}

/** Total que será dividido: em parcelas por "valor da parcela" é valor × N (research R5). */
export function totalDividido(
  cents: number,
  parcelas: number,
  modoParcela: 'total_amount' | 'installment_amount',
): number {
  return parcelas > 1 && modoParcela === 'installment_amount' ? cents * parcelas : cents;
}

export interface CalculoDivisao {
  /** Valor de cada parte em centavos (`null` = ainda inválido/em branco). */
  valores: (number | null)[];
  /** Total − soma das partes (só `amounts`; positivo = faltam, negativo = sobram). */
  diferenca: number;
  /** Texto "Faltam R$ X" / "Sobram R$ X"; `null` quando fecha. */
  aviso: string | null;
  /** Motivo de bloquear o salvar; `null` = pode salvar. */
  erro: string | null;
}

/** Calcula os valores das partes pela porta TS do `MoneySplitter` (RT-02) e a diferença (US4.3). */
export function calcularDivisao(
  modo: ModoDivisao,
  totalCents: number | null,
  partes: readonly ParteDivisao[],
): CalculoDivisao {
  if (partes.length < PARTES_MIN || partes.length > PARTES_MAX) {
    return {
      valores: partes.map(() => null),
      diferenca: 0,
      aviso: null,
      erro: `Divida em ${PARTES_MIN} a ${PARTES_MAX} partes.`,
    };
  }

  const repetida = partes.some((p, i) => partes.findIndex((q) => q.tagId === p.tagId) !== i);
  if (repetida) {
    return {
      valores: partes.map(() => null),
      diferenca: 0,
      aviso: null,
      erro: 'Não repita a mesma pessoa nas partes.',
    };
  }

  if (partes.some((p, i) => p.tagId === null && partes.findIndex((q) => q.tagId === null) !== i)) {
    return {
      valores: partes.map(() => null),
      diferenca: 0,
      aviso: null,
      erro: 'Só uma parte "Eu".',
    };
  }

  if (totalCents === null || totalCents <= 0) {
    return {
      valores: partes.map(() => null),
      diferenca: 0,
      aviso: null,
      erro: 'Informe o valor total.',
    };
  }

  if (modo === 'equal') {
    if (totalCents < partes.length) {
      return {
        valores: partes.map(() => null),
        diferenca: 0,
        aviso: null,
        erro: 'O valor é pequeno demais para essa divisão.',
      };
    }
    return {
      valores: splitMoney(totalCents, partes.length),
      diferenca: 0,
      aviso: null,
      erro: null,
    };
  }

  const valores = partes.map((p) => {
    const r = parseValueExpression(p.valor);
    return r.ok ? r.cents : null;
  });
  if (valores.some((v) => v === null)) {
    return { valores, diferenca: 0, aviso: null, erro: 'Informe o valor de cada parte.' };
  }
  const soma = (valores as number[]).reduce((a, b) => a + b, 0);
  const diferenca = totalCents - soma;
  if (diferenca === 0) return { valores, diferenca, aviso: null, erro: null };
  const aviso =
    diferenca > 0 ? `Faltam ${formatBrl(diferenca)}` : `Sobram ${formatBrl(-diferenca)}`;
  return { valores, diferenca, aviso, erro: aviso };
}

/** Corpo `split` do contrato: em `equal` só as tags; em `amounts` o valor como digitado. */
export function montarSplit(modo: ModoDivisao, partes: readonly ParteDivisao[]): SplitInput {
  return {
    mode: modo,
    parts: partes.map((p) =>
      modo === 'amounts'
        ? { settlementTagId: p.tagId, amount: p.valor.trim() }
        : { settlementTagId: p.tagId },
    ),
  };
}
