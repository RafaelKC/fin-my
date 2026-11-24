/** Chaves das etapas no contrato (`PUT /onboarding/steps/{step}`, 006 research R3). */
export type EtapaChave = 'wallets' | 'cards' | 'settlement_tags' | 'installments' | 'recurrences';

export type EstadoEtapa = 'done' | 'skipped';

/** `GET /onboarding` (006 contracts/api.md). */
export interface OnboardingState {
  steps: Partial<Record<EtapaChave, EstadoEtapa>>;
  dismissed: boolean;
  autoStart: boolean;
}

/** Uma etapa do guia: chave da API, trecho da URL e textos da US3.1. */
export interface Etapa {
  chave: EtapaChave;
  slug: string;
  titulo: string;
  explicacao: string;
}

/** As 5 etapas, na ordem do guia (US3.1). */
export const ETAPAS: readonly Etapa[] = [
  {
    chave: 'wallets',
    slug: 'carteiras',
    titulo: 'Carteiras',
    explicacao:
      'Cadastre cada conta ou carteira onde você guarda dinheiro e informe o saldo que está na conta hoje.',
  },
  {
    chave: 'cards',
    slug: 'cartoes',
    titulo: 'Cartões',
    explicacao: 'Cadastre seus cartões de crédito com o dia de fechamento e o de vencimento.',
  },
  {
    chave: 'settlement_tags',
    slug: 'pessoas',
    titulo: 'Pessoas que te devolvem dinheiro',
    explicacao:
      'Quem costuma te devolver dinheiro de gastos divididos? Cadastre cada pessoa para acompanhar quanto ela te deve.',
  },
  {
    chave: 'installments',
    slug: 'parcelamentos',
    titulo: 'Parcelamentos em andamento',
    explicacao:
      'Compras parceladas no cartão que ainda têm parcelas a pagar: marque "Parcelamento em andamento" e informe a próxima parcela.',
  },
  {
    chave: 'recurrences',
    slug: 'contas-fixas',
    titulo: 'Contas fixas e salário',
    explicacao: 'Cadastre o que se repete todo mês, como salário, aluguel e assinaturas.',
  },
];

export function etapaPorSlug(slug: string | null): number {
  return ETAPAS.findIndex((e) => e.slug === slug);
}
