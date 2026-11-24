import {
  etapaInicial,
  indiceDaPrimeiraPendente,
  rotaDaEtapa,
  temGuiaPendente,
} from './onboarding-logic';
import { OnboardingState } from './onboarding.models';

const estado = (parcial: Partial<OnboardingState> = {}): OnboardingState => ({
  steps: {},
  dismissed: false,
  autoStart: false,
  ...parcial,
});

describe('onboarding-logic', () => {
  it('a primeira pendente ignora etapas concluídas ou puladas', () => {
    const s = estado({ steps: { wallets: 'done', cards: 'skipped' } });
    expect(indiceDaPrimeiraPendente(s)).toBe(2);
    expect(rotaDaEtapa(etapaInicial(s))).toBe('/primeiros-passos/pessoas');
  });

  it('com tudo resolvido não há pendente e o guia volta à primeira etapa', () => {
    const s = estado({
      steps: {
        wallets: 'done',
        cards: 'done',
        settlement_tags: 'skipped',
        installments: 'skipped',
        recurrences: 'done',
      },
    });
    expect(indiceDaPrimeiraPendente(s)).toBe(-1);
    expect(temGuiaPendente(s)).toBe(false);
    expect(rotaDaEtapa(etapaInicial(s))).toBe('/primeiros-passos/carteiras');
  });

  it('guia dispensado não tem pendência (US3.3)', () => {
    expect(temGuiaPendente(estado({ dismissed: true }))).toBe(false);
    expect(temGuiaPendente(estado())).toBe(true);
  });
});
