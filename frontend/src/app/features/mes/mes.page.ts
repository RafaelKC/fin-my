import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router } from '@angular/router';
import { EMPTY, Subject, catchError, combineLatest, merge, of, switchMap, tap } from 'rxjs';
import { problemMessage } from '../../core/errors/problem-details';
import {
  BrlPipe,
  isYearMonth,
  monthLabel,
  nextMonth,
  previousMonth,
  today,
} from '../../core/money';
import { Wallet } from '../carteiras/wallet.models';
import { WalletsService } from '../carteiras/wallets.service';
import { EntriesService } from '../lancamento/entries.service';
import { Entry } from '../lancamento/entry.models';
import { ExclusaoService } from '../lancamento/exclusao.service';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { TransfersService } from '../lancamento/transfers.service';
import {
  ColunaOrdem,
  FiltroSituacao,
  MES_MAXIMO,
  MES_MINIMO,
  Ordem,
  alternarOrdem,
  direcaoDoDeslize,
  estaAtrasada,
  filtrarPorSituacao,
  formatarData,
  lerCarteiras,
  lerSituacao,
  montarQuery,
  ordenar,
} from './mes-logic';
import { MonthView, SummaryColumn, Transfer } from './month.models';
import { MonthsService } from './months.service';
import { PendenciasComponent } from './pendencias/pendencias.component';
import { PendenciasService } from './pendencias/pendencias.service';

const DESKTOP = '(min-width: 1024px)';

interface LinhaResumo {
  id: string;
  rotulo: string;
  nota?: string;
  previsto: number;
  realizado: number;
  /** FR-017: Resultado e Saldo final negativos em vermelho. */
  destacaNegativo: boolean;
}

/**
 * Visão do mês (US3; FR-014..FR-018, FR-026; contracts/ui-rotas.md): resumo Previsto | Realizado,
 * listas de Entradas, Saídas e Transferências, caixinha de situação de um toque, navegação por
 * setas e deslizar (filtros ficam na query string `carteiras` e `situacao`). A lista vira tabela
 * ordenável a partir de 1024 px. O aviso de pendências (T014) é do componente `app-pendencias`; o recorte por carteira das transferências
 * (T016) ficam para as próximas tarefas.
 */
@Component({
  selector: 'app-mes-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrlPipe, MatIconModule, PendenciasComponent],
  templateUrl: './mes.page.html',
  styleUrl: './mes.page.scss',
})
export class MesPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly months = inject(MonthsService);
  private readonly entries = inject(EntriesService);
  private readonly transfersApi = inject(TransfersService);
  private readonly wallets = inject(WalletsService);
  private readonly launcher = inject(LancamentoLauncher);
  private readonly pendencias = inject(PendenciasService);
  private readonly exclusao = inject(ExclusaoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly recarregar$ = new Subject<void>();

  protected readonly mes = signal('');
  protected readonly carteirasSelecionadas = signal<string[]>([]);
  protected readonly situacao = signal<FiltroSituacao>(null);
  protected readonly visao = signal<MonthView | null>(null);
  protected readonly carregando = signal(true);
  protected readonly erro = signal<string | null>(null);
  protected readonly erroAcao = signal<string | null>(null);
  protected readonly carteiras = signal<Wallet[]>([]);
  protected readonly ordem = signal<Ordem | null>(null);
  protected readonly transferenciasAbertas = signal(false);
  protected readonly alterandoId = signal<string | null>(null);
  protected readonly desktop = signal(false);

  protected readonly rotuloMes = computed(() => (this.mes() ? monthLabel(this.mes()) : ''));
  /** FR-019: o aviso só aparece no mês de hoje e quando há pendências. */
  protected readonly mostraPendencias = computed(() => {
    const v = this.visao();
    return v !== null && v.month === v.today.slice(0, 7) && v.pending.count > 0;
  });
  protected readonly noMinimo = computed(() => this.mes() <= MES_MINIMO);
  protected readonly noMaximo = computed(() => this.mes() >= MES_MAXIMO);

  private readonly porId = computed(() => new Map(this.carteiras().map((w) => [w.id, w])));

  protected readonly entradas = computed(() => this.lista('income'));
  protected readonly saidas = computed(() => this.lista('expense'));
  /** Transferências são sempre efetivadas: somem no filtro "Previstas". */
  protected readonly transferencias = computed(() =>
    this.situacao() === 'planned' ? [] : (this.visao()?.transfers ?? []),
  );
  protected readonly mesVazio = computed(() => {
    const v = this.visao();
    return v !== null && v.entries.length === 0 && v.transfers.length === 0;
  });
  protected readonly semResultadoNoFiltro = computed(
    () =>
      this.visao() !== null &&
      !this.mesVazio() &&
      this.entradas().length === 0 &&
      this.saidas().length === 0 &&
      this.transferencias().length === 0,
  );

  protected readonly linhasResumo = computed<LinhaResumo[]>(() => {
    const v = this.visao();
    if (v === null) return [];
    const par = (
      id: string,
      rotulo: string,
      campo: keyof SummaryColumn,
      extra: Partial<LinhaResumo> = {},
    ): LinhaResumo => ({
      id,
      rotulo,
      previsto: v.planned[campo],
      realizado: v.actual[campo],
      destacaNegativo: false,
      ...extra,
    });
    const comSaldoDeCarteira =
      v.planned.walletsInitialBalanceCents !== 0 || v.actual.walletsInitialBalanceCents !== 0;
    // Com uma única carteira filtrada, as transferências entram no resumo (contracts/ui-rotas.md);
    // com várias, só quando alguma cruza o filtro (senão o saldo final não fecha com as linhas).
    const comTransferencias =
      this.carteirasSelecionadas().length === 1 ||
      [v.planned, v.actual].some((c) => c.transfersInCents !== 0 || c.transfersOutCents !== 0);
    return [
      par('saldo-inicial', 'Saldo inicial', 'openingBalanceCents', {
        nota: comSaldoDeCarteira ? 'inclui saldo inicial de carteiras' : undefined,
      }),
      par('entradas', 'Entradas', 'incomeCents'),
      par('saidas', 'Saídas', 'expenseCents'),
      par('resultado', 'Resultado', 'resultCents', { destacaNegativo: true }),
      ...(comTransferencias
        ? [
            par('transferencias-recebidas', 'Transferências recebidas', 'transfersInCents'),
            par('transferencias-enviadas', 'Transferências enviadas', 'transfersOutCents'),
          ]
        : []),
      par('saldo-final', 'Saldo final', 'finalBalanceCents', { destacaNegativo: true }),
    ];
  });

  private chave = '';
  private inicioDoDeslize: { x: number; y: number } | null = null;

  constructor() {
    const consulta = typeof matchMedia === 'function' ? matchMedia(DESKTOP) : null;
    if (consulta) {
      this.desktop.set(consulta.matches);
      const aoMudar = (e: { matches: boolean }) => this.desktop.set(e.matches);
      consulta.addEventListener?.('change', aoMudar);
      this.destroyRef.onDestroy(() => consulta.removeEventListener?.('change', aoMudar));
    }

    this.recarregar$
      .pipe(
        switchMap(() =>
          this.months.get(this.mes(), this.carteirasSelecionadas()).pipe(
            tap(() => this.erro.set(null)),
            catchError((error: unknown) => {
              this.erro.set(problemMessage(error));
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((visao) => {
        this.carregando.set(false);
        if (visao !== null) this.visao.set(visao);
      });

    combineLatest([this.route.paramMap, this.route.queryParamMap])
      .pipe(takeUntilDestroyed())
      .subscribe(([params, query]) => {
        const mes = params.get('mes') ?? '';
        if (!isYearMonth(mes) || mes < MES_MINIMO || mes > MES_MAXIMO) {
          void this.router.navigate(['/mes', today().slice(0, 7)], { replaceUrl: true });
          return;
        }
        const carteiras = lerCarteiras(query.get('carteiras'));
        this.mes.set(mes);
        this.carteirasSelecionadas.set(carteiras);
        this.situacao.set(lerSituacao(query.get('situacao')));

        // Só o mês e as carteiras vão à API; a situação filtra as listas aqui mesmo.
        const chave = `${mes}|${carteiras.join(',')}`;
        if (chave === this.chave) return;
        this.chave = chave;
        this.visao.set(null);
        this.carregando.set(true);
        this.erro.set(null);
        this.recarregar$.next();
      });

    // FR-016: o que mudar um lançamento (aqui, no formulário rápido) recarrega a visão.
    merge(
      this.entries.criados$,
      this.entries.alterados$,
      this.pendencias.alteradas$,
      this.transfersApi.criadas$,
      this.exclusao.mudou$,
    )
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.recarregar$.next());
    merge(this.entries.criados$, this.transfersApi.criadas$)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.carregarCarteiras());

    this.carregarCarteiras();
  }

  protected nomeCarteira(id: string): string {
    return this.porId().get(id)?.name ?? '';
  }

  protected corCarteira(id: string): string {
    return this.porId().get(id)?.color ?? 'transparent';
  }

  protected data(data: string): string {
    return formatarData(data);
  }

  /**
   * Com uma única carteira filtrada a transferência é "enviada" ou "recebida" (US5.2); sem filtro
   * (ou com várias) é só "Transferência": o De → Para já aparece na linha.
   */
  protected rotuloTransferencia(t: Transfer): string {
    const selecionadas = this.carteirasSelecionadas();
    if (selecionadas.length !== 1) return 'Transferência';
    return t.fromWalletId === selecionadas[0] ? 'Transferência enviada' : 'Transferência recebida';
  }

  protected atrasada(entry: Entry): boolean {
    const hoje = this.visao()?.today ?? today();
    return estaAtrasada(entry, hoje);
  }

  protected rotuloRealizada(entry: Entry): string {
    return entry.kind === 'income' ? 'Recebida' : 'Paga';
  }

  protected rotuloSituacao(entry: Entry): string {
    return entry.status === 'planned' ? 'Prevista' : this.rotuloRealizada(entry);
  }

  protected tentarDeNovo(): void {
    this.carregando.set(true);
    this.erro.set(null);
    this.recarregar$.next();
  }

  protected ir(passo: -1 | 1): void {
    const destino = passo < 0 ? previousMonth(this.mes()) : nextMonth(this.mes());
    if (destino < MES_MINIMO || destino > MES_MAXIMO) return;
    void this.router.navigate(['/mes', destino], { queryParamsHandling: 'preserve' });
  }

  protected alternarCarteira(id: string): void {
    const atual = this.carteirasSelecionadas();
    const proximas = atual.includes(id) ? atual.filter((x) => x !== id) : [...atual, id];
    this.filtrar(proximas, this.situacao());
  }

  protected limparCarteiras(): void {
    this.filtrar([], this.situacao());
  }

  protected escolherSituacao(situacao: FiltroSituacao): void {
    this.filtrar(this.carteirasSelecionadas(), situacao);
  }

  protected ordenarPor(coluna: ColunaOrdem): void {
    this.ordem.update((atual) => alternarOrdem(atual, coluna));
  }

  protected ariaSort(coluna: ColunaOrdem): 'ascending' | 'descending' | 'none' {
    const ordem = this.ordem();
    if (ordem?.coluna !== coluna) return 'none';
    return ordem.direcao === 'asc' ? 'ascending' : 'descending';
  }

  /** Caixinha de um toque (FR-006): PATCH da situação; o resumo recarrega sozinho (FR-016). */
  protected alternarSituacao(entry: Entry): void {
    if (this.alterandoId() !== null) return;
    this.alterandoId.set(entry.id);
    this.erroAcao.set(null);
    this.entries.setStatus(entry.id, entry.status === 'planned' ? 'settled' : 'planned').subscribe({
      next: () => this.alterandoId.set(null),
      error: (error: unknown) => {
        this.alterandoId.set(null);
        this.erroAcao.set(problemMessage(error));
        this.recarregar$.next(); // desfaz a marcação visual da caixinha
      },
    });
  }

  /** Tocar no item abre o formulário rápido em modo edição (a edição completa é da T018). */
  protected editar(entry: Entry): void {
    this.launcher.abrir(entry);
  }

  /** RT-09: exclui a transferência e oferece "Desfazer" por 8 s. */
  protected excluirTransferencia(transfer: Transfer): void {
    if (this.alterandoId() !== null) return;
    this.alterandoId.set(transfer.id);
    this.erroAcao.set(null);
    this.exclusao.excluir('transfer', transfer.id).subscribe({
      next: (resultado) => {
        this.alterandoId.set(null);
        this.exclusao.oferecerDesfazer(resultado.deletionBatchId);
      },
      error: (error: unknown) => {
        this.alterandoId.set(null);
        this.erroAcao.set(problemMessage(error));
      },
    });
  }

  protected iniciarDeslize(evento: PointerEvent): void {
    if (evento.pointerType === 'mouse') return;
    this.inicioDoDeslize = { x: evento.clientX, y: evento.clientY };
  }

  protected terminarDeslize(evento: PointerEvent): void {
    const inicio = this.inicioDoDeslize;
    this.inicioDoDeslize = null;
    if (inicio === null) return;
    const direcao = direcaoDoDeslize(evento.clientX - inicio.x, evento.clientY - inicio.y);
    if (direcao !== 0) this.ir(direcao);
  }

  protected cancelarDeslize(): void {
    this.inicioDoDeslize = null;
  }

  private filtrar(carteiras: readonly string[], situacao: FiltroSituacao): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: montarQuery(carteiras, situacao),
    });
  }

  private lista(kind: Entry['kind']): Entry[] {
    const todas = this.visao()?.entries ?? [];
    const filtradas = filtrarPorSituacao(
      todas.filter((e) => e.kind === kind),
      this.situacao(),
    );
    return this.desktop()
      ? ordenar(filtradas, this.ordem(), (id) => this.nomeCarteira(id))
      : filtradas;
  }

  private carregarCarteiras(): void {
    this.wallets
      .list('all')
      .pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((lista) => this.carteiras.set(lista));
  }
}
