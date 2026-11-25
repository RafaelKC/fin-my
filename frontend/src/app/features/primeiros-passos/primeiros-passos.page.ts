import { ZardButtonComponent } from '@/shared/components/button';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable } from 'rxjs';
import { problemMessage } from '../../core/errors/problem-details';
import { today } from '../../core/money';
import { CartaoFormPage } from '../cartoes/cartao-form.page';
import { CarteiraFormPage } from '../carteiras/carteira-form.page';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { RecorrenciaFormPage } from '../recorrencias/recorrencia-form.page';
import { etapaInicial, rotaDaEtapa } from './onboarding-logic';
import { ETAPAS, EstadoEtapa, Etapa, OnboardingState, etapaPorSlug } from './onboarding.models';
import { OnboardingService } from './onboarding.service';
import { PessoaForm } from './pessoa-form';

/**
 * Tela `/primeiros-passos/:etapa` (006 US3, FR-007): guia de 5 etapas que reaproveita os formulários
 * de carteira, cartão, tag de acerto, compra em andamento e recorrência. "Continuar" marca a etapa
 * como concluída, "Pular" como pulada e "Dispensar guia" encerra o guia; ao final vai ao mês de hoje.
 */
@Component({
  selector: 'app-primeiros-passos-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CarteiraFormPage, CartaoFormPage, RecorrenciaFormPage, PessoaForm, ZardButtonComponent],
  templateUrl: './primeiros-passos.page.html',
  styleUrl: './primeiros-passos.page.scss',
})
export class PrimeirosPassosPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(OnboardingService);
  private readonly launcher = inject(LancamentoLauncher);

  protected readonly etapas = ETAPAS;
  protected readonly indice = signal(0);
  protected readonly etapa = computed<Etapa>(() => ETAPAS[this.indice()]);
  protected readonly ultima = computed(() => this.indice() === ETAPAS.length - 1);
  protected readonly estado = signal<OnboardingState | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);
  /** Quantos itens a pessoa já cadastrou em cada etapa nesta sessão do guia. */
  protected readonly cadastrados = signal<Record<string, number>>({});
  /** Desliga e religa o formulário embutido a cada cadastro, para ele voltar limpo. */
  protected readonly formularioVisivel = signal(true);
  protected readonly nomesDePessoas = signal<string[]>([]);

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const i = etapaPorSlug(params.get('etapa'));
      if (i >= 0) {
        this.indice.set(i);
        this.erro.set(null);
        return;
      }
      // Etapa desconhecida: abre na primeira pendente (ou na primeira, se o estado não carregar).
      this.service.get().subscribe({
        next: (estado) => this.irParaEtapa(etapaInicial(estado), true),
        error: () => this.irParaEtapa(ETAPAS[0], true),
      });
    });
    this.service
      .get()
      .pipe(takeUntilDestroyed())
      .subscribe({ next: (estado) => this.estado.set(estado), error: () => undefined });
  }

  protected contar(chave: string): number {
    return this.cadastrados()[chave] ?? 0;
  }

  /** Um item novo foi salvo na etapa: recria o formulário limpo para o próximo. */
  protected cadastrou(nome?: string): void {
    const chave = this.etapa().chave;
    this.cadastrados.update((c) => ({ ...c, [chave]: (c[chave] ?? 0) + 1 }));
    if (nome !== undefined) this.nomesDePessoas.update((n) => [...n, nome]);
    this.formularioVisivel.set(false);
    setTimeout(() => this.formularioVisivel.set(true));
  }

  protected abrirParcelamento(): void {
    this.launcher
      .abrir()
      .afterClosed()
      .subscribe((entry) => {
        if (entry !== undefined) this.cadastrou();
      });
  }

  protected continuar(): void {
    this.marcar('done');
  }

  protected pular(): void {
    this.marcar('skipped');
  }

  protected dispensar(): void {
    this.executar(this.service.dismiss(), () => this.irParaMesDeHoje());
  }

  private marcar(estado: EstadoEtapa): void {
    this.executar(this.service.setStep(this.etapa().chave, estado), () => {
      if (this.ultima()) this.irParaMesDeHoje();
      else this.irParaEtapa(ETAPAS[this.indice() + 1], false);
    });
  }

  private irParaEtapa(etapa: Etapa, substituir: boolean): void {
    void this.router.navigateByUrl(rotaDaEtapa(etapa), { replaceUrl: substituir });
  }

  private irParaMesDeHoje(): void {
    void this.router.navigateByUrl(`/mes/${today().slice(0, 7)}`);
  }

  private executar(chamada: Observable<OnboardingState>, aoConcluir: () => void): void {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    this.erro.set(null);
    chamada.subscribe({
      next: (estado) => {
        this.ocupado.set(false);
        this.estado.set(estado);
        aoConcluir();
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }
}
