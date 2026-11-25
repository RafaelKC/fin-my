import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Icone } from '@/shared/ui/icone';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, merge, of, switchMap, tap } from 'rxjs';
import { problemMessage } from '../../core/errors/problem-details';
import {
  BrlPipe,
  isYearMonth,
  monthLabel,
  nextMonth,
  previousMonth,
  today,
} from '../../core/money';
import { EntriesService } from '../lancamento/entries.service';
import { ExclusaoService } from '../lancamento/exclusao.service';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { MES_MAXIMO, MES_MINIMO, formatarData } from '../mes/mes-logic';
import {
  NumeroDetalhavel,
  ehCredito,
  itensDoNumero,
  montarCobranca,
  podeGerarCobranca,
} from './acertos-logic';
import { SettlementItem, SettlementRow } from './settlement.models';
import { SettlementsService } from './settlements.service';
import { ZardButtonComponent } from '@/shared/components/button';

const ROTULO_NUMERO: Record<NumeroDetalhavel, string> = {
  spent: 'Gasto',
  charged: 'Cobrado',
  received: 'Recebido',
};

/** Número aberto (lista de lançamentos) de um cartão de acerto. */
interface Aberto {
  tagId: string;
  numero: NumeroDetalhavel;
}

/**
 * Tela `/acertos/:mes` (US2, US3; FR-009, FR-010; contracts/ui-rotas.md): um cartão por Tag de
 * acerto com Gasto, Cobrado, Recebido, A cobrar, A receber e Saldo acumulado; tocar em Gasto,
 * Cobrado ou Recebido abre a lista de lançamentos que o compõem; "Gerar cobrança" abre o
 * formulário rápido de Entrada pré-preenchido (nada é salvo sem "Salvar").
 */
@Component({
  selector: 'app-acertos-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ZardButtonComponent, BrlPipe, Icone, RouterLink],
  templateUrl: './acertos.page.html',
  styleUrl: './acertos.page.scss',
})
export class AcertosPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly settlements = inject(SettlementsService);
  private readonly launcher = inject(LancamentoLauncher);
  private readonly entries = inject(EntriesService);
  private readonly exclusao = inject(ExclusaoService);
  private readonly recarregar$ = new Subject<void>();

  protected readonly mes = signal('');
  protected readonly linhas = signal<SettlementRow[] | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly aberto = signal<Aberto | null>(null);
  protected readonly itens = signal<SettlementItem[] | null>(null);
  protected readonly erroItens = signal<string | null>(null);

  protected readonly rotuloMes = computed(() => (this.mes() ? monthLabel(this.mes()) : ''));
  protected readonly noMinimo = computed(() => this.mes() <= MES_MINIMO);
  protected readonly noMaximo = computed(() => this.mes() >= MES_MAXIMO);
  protected readonly itensAbertos = computed(() => {
    const a = this.aberto();
    const todos = this.itens();
    return a === null || todos === null ? [] : itensDoNumero(todos, a.numero);
  });

  protected readonly formatarData = formatarData;
  protected readonly podeGerarCobranca = podeGerarCobranca;
  protected readonly ehCredito = ehCredito;
  protected readonly abs = Math.abs;

  constructor() {
    this.recarregar$
      .pipe(
        switchMap(() =>
          this.settlements.list(this.mes()).pipe(
            tap(() => this.erro.set(null)),
            catchError((error: unknown) => {
              this.erro.set(problemMessage(error));
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((linhas) => {
        if (linhas === null) return;
        this.linhas.set(linhas);
        this.recarregarAberto();
      });

    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const mes = params.get('mes') ?? '';
      if (!isYearMonth(mes) || mes < MES_MINIMO || mes > MES_MAXIMO) {
        void this.router.navigate(['/acertos', today().slice(0, 7)], { replaceUrl: true });
        return;
      }
      this.mes.set(mes);
      this.linhas.set(null);
      this.fecharLista();
      this.recarregar$.next();
    });

    // O que muda um lançamento (inclusive a cobrança gerada aqui) refaz os números.
    merge(this.entries.criados$, this.entries.alterados$, this.exclusao.mudou$)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.recarregar$.next());
  }

  protected rotulo(numero: NumeroDetalhavel): string {
    return ROTULO_NUMERO[numero];
  }

  protected ir(delta: -1 | 1): void {
    const destino = delta < 0 ? previousMonth(this.mes()) : nextMonth(this.mes());
    void this.router.navigate(['/acertos', destino]);
  }

  protected estaAberto(row: SettlementRow, numero: NumeroDetalhavel): boolean {
    const a = this.aberto();
    return a !== null && a.tagId === row.tagId && a.numero === numero;
  }

  /** Tocar de novo no mesmo número fecha a lista; em outro, troca. */
  protected alternar(row: SettlementRow, numero: NumeroDetalhavel): void {
    if (this.estaAberto(row, numero)) {
      this.fecharLista();
      return;
    }
    const mudouDeTag = this.aberto()?.tagId !== row.tagId;
    this.aberto.set({ tagId: row.tagId, numero });
    if (mudouDeTag || this.itens() === null) this.carregarItens(row.tagId);
  }

  protected gerarCobranca(row: SettlementRow): void {
    // A tela recarrega pelo `criados$` quando a pessoa salva (US3.1).
    this.launcher.abrirPreenchida(montarCobranca(row, this.mes(), today()));
  }

  protected linhaAberta(row: SettlementRow): boolean {
    return this.aberto()?.tagId === row.tagId;
  }

  private fecharLista(): void {
    this.aberto.set(null);
    this.itens.set(null);
    this.erroItens.set(null);
  }

  private recarregarAberto(): void {
    const a = this.aberto();
    if (a !== null) this.carregarItens(a.tagId);
  }

  private carregarItens(tagId: string): void {
    this.itens.set(null);
    this.erroItens.set(null);
    this.settlements.items(this.mes(), tagId).subscribe({
      next: (lista) => {
        if (this.aberto()?.tagId === tagId) this.itens.set(lista);
      },
      error: (error: unknown) => this.erroItens.set(problemMessage(error)),
    });
  }
}
