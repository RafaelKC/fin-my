import { ETAPAS, Etapa, OnboardingState } from './onboarding.models';

/** Índice da primeira etapa nem concluída nem pulada; `-1` quando não há mais nenhuma. */
export function indiceDaPrimeiraPendente(estado: OnboardingState): number {
  return ETAPAS.findIndex((e) => estado.steps[e.chave] === undefined);
}

/** Cartão "Continuar configuração" do Painel: guia não dispensado e com etapa pendente. */
export function temGuiaPendente(estado: OnboardingState): boolean {
  return !estado.dismissed && indiceDaPrimeiraPendente(estado) >= 0;
}

/** Etapa em que o guia deve abrir (a primeira pendente; a primeira de todas se não houver). */
export function etapaInicial(estado: OnboardingState): Etapa {
  return ETAPAS[Math.max(0, indiceDaPrimeiraPendente(estado))];
}

export function rotaDaEtapa(etapa: Etapa): string {
  return `/primeiros-passos/${etapa.slug}`;
}
