import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Icone } from '@/shared/ui/icone';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';
import { problemMessage } from '../../core/errors/problem-details';
import { BrlPipe, today } from '../../core/money';
import { ChartAnual } from '../../shared/chart-anual/chart-anual';
import { formatarData } from '../mes/mes-logic';
import { etapaInicial, rotaDaEtapa, temGuiaPendente } from '../primeiros-passos/onboarding-logic';
import { OnboardingState } from '../primeiros-passos/onboarding.models';
import { OnboardingService } from '../primeiros-passos/onboarding.service';
import {
  ANO_MAXIMO,
  ANO_MINIMO,
  Leitura,
  ROTULO_SITUACAO_FATURA,
  ehCreditoDeAcerto,
  lerAno,
  linhasDoPainel,
  mesDeHojeNoAno,
  pontosDoGrafico,
} from './painel-logic';
import { NowView, YearPanel } from './panel.models';
import { PanelService } from './panel.service';

/**
 * Tela `/painel/:ano` (006 US1, US2; FR-001 a FR-005): "Agora" no topo (saldos, faturas próximas,
 * pendências, acertos), tabela dos 12 meses em Previsto ou Realizado com total do ano, mês de hoje
 * destacado, negativos em vermelho e gráfico SVG. Não calcula nada: exibe o que a API devolve.
 */
@Component({
  selector: 'app-painel-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrlPipe, ChartAnual, Icone, RouterLink],
  templateUrl: './painel.page.html',
  styleUrl: './painel.page.scss',
})
export class PainelPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(PanelService);
  private readonly onboarding = inject(OnboardingService);

  protected readonly ano = signal(0);
  protected readonly leitura = signal<Leitura>('planned');
  protected readonly painel = signal<YearPanel | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly agora = signal<NowView | null>(null);
  protected readonly erroAgora = signal<string | null>(null);
  private readonly guia = signal<OnboardingState | null>(null);
  /** Rota do cartão "Continuar configuração" (006 US3.3); `null` sem guia pendente. */
  protected readonly rotaDoGuia = computed(() => {
    const g = this.guia();
    return g !== null && temGuiaPendente(g) ? rotaDaEtapa(etapaInicial(g)) : null;
  });

  protected readonly noMinimo = computed(() => this.ano() <= ANO_MINIMO);
  protected readonly noMaximo = computed(() => this.ano() >= ANO_MAXIMO);
  protected readonly linhas = computed(() => {
    const p = this.painel();
    return p === null ? [] : linhasDoPainel(p, this.leitura());
  });
  protected readonly totais = computed(() => this.painel()?.totals[this.leitura()] ?? null);
  protected readonly pontos = computed(() => pontosDoGrafico(this.linhas()));
  protected readonly mesDeHoje = computed(() => {
    const hoje = this.painel()?.today ?? this.agora()?.today ?? null;
    return hoje === null ? null : mesDeHojeNoAno(hoje, this.ano());
  });
  protected readonly mesAtual = computed(() => (this.agora()?.today ?? today()).slice(0, 7));
  protected readonly descricaoGrafico = computed(
    () => `${this.leitura() === 'planned' ? 'Previsto' : 'Realizado'} de ${this.ano()}`,
  );

  protected readonly rotuloSituacao = ROTULO_SITUACAO_FATURA;
  protected readonly formatarData = formatarData;
  protected readonly ehCredito = ehCreditoDeAcerto;
  protected readonly abs = Math.abs;

  constructor() {
    this.onboarding
      .get()
      .pipe(takeUntilDestroyed())
      .subscribe({ next: (g) => this.guia.set(g), error: () => undefined });

    this.service
      .now()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (agora) => this.agora.set(agora),
        error: (error: unknown) => this.erroAgora.set(problemMessage(error)),
      });

    this.route.paramMap
      .pipe(
        switchMap((params) => {
          const ano = lerAno(params.get('ano'));
          if (ano === null) {
            void this.router.navigate(['/painel', today().slice(0, 4)], { replaceUrl: true });
            return of(null);
          }
          this.ano.set(ano);
          this.painel.set(null);
          this.erro.set(null);
          return this.service.year(ano).pipe(
            catchError((error: unknown) => {
              this.erro.set(problemMessage(error));
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((painel) => {
        if (painel !== null) this.painel.set(painel);
      });
  }

  protected ir(delta: -1 | 1): void {
    void this.router.navigate(['/painel', this.ano() + delta]);
  }

  protected trocarLeitura(leitura: Leitura): void {
    this.leitura.set(leitura);
  }

  /** Tocar em qualquer parte da linha abre o mês; o link da linha navega sozinho. */
  protected abrirMes(mes: string, evento: Event): void {
    if ((evento.target as HTMLElement).closest('a') !== null) return;
    void this.router.navigate(['/mes', mes]);
  }
}
