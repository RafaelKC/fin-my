import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subject, catchError, merge, of, switchMap, tap, timer } from 'rxjs';
import { problemMessage } from '../../core/errors/problem-details';
import { BrlPipe } from '../../core/money';
import { TagFilter } from '../../shared/tag-picker/tag-filter';
import { Wallet } from '../carteiras/wallet.models';
import { WalletsService } from '../carteiras/wallets.service';
import { CardPurchase } from '../lancamento/card-purchase.models';
import { CardsService } from '../cartoes/cards.service';
import { CardPurchasesService } from '../lancamento/card-purchases.service';
import { EntriesService } from '../lancamento/entries.service';
import {
  Entry,
  EntryStatus,
  LedgerItem,
  LedgerKind,
  SearchResult,
} from '../lancamento/entry.models';
import { ExclusaoService } from '../lancamento/exclusao.service';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { Transfer } from '../mes/month.models';

/** Tamanho da página da busca; as demais vêm pelo cursor (research R10). */
export const TAMANHO_PAGINA = 50;

export const TIPOS_BUSCA: readonly { value: LedgerKind; rotulo: string }[] = [
  { value: 'income', rotulo: 'Entradas' },
  { value: 'expense', rotulo: 'Saídas' },
  { value: 'transfer', rotulo: 'Transferências' },
  { value: 'card_purchase', rotulo: 'Compras no cartão' },
];

/** `AAAA-MM-DD` → `DD/MM/AAAA` (RT-10). */
function dataBr(iso: string): string {
  const [a, m, d] = iso.split('-');
  return `${d}/${m}/${a}`;
}

/**
 * Busca (US6.5; FR-025): texto + filtros de tipo, carteira, situação e período; lista com rolagem
 * infinita pelo cursor e o total de TODOS os resultados. Tocar numa entrada ou saída abre o
 * formulário rápido em modo edição.
 */
@Component({
  selector: 'app-busca-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, BrlPipe, TagFilter],
  templateUrl: './busca.page.html',
  styleUrl: './busca.page.scss',
})
export class BuscaPage {
  private readonly entries = inject(EntriesService);
  private readonly wallets = inject(WalletsService);
  private readonly cards = inject(CardsService);
  private readonly launcher = inject(LancamentoLauncher);
  private readonly exclusao = inject(ExclusaoService);
  private readonly compras = inject(CardPurchasesService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fim = viewChild<ElementRef<HTMLElement>>('fim');

  protected readonly tipos = TIPOS_BUSCA;
  protected readonly q = signal('');
  protected readonly kinds = signal<LedgerKind[]>([]);
  protected readonly walletIds = signal<string[]>([]);
  /** Filtro de tags (FR-004): lançamentos e compras com qualquer uma delas. */
  protected readonly tagIds = signal<string[]>([]);
  protected readonly status = signal<EntryStatus | ''>('');
  protected readonly from = signal('');
  protected readonly to = signal('');

  protected readonly carteiras = signal<Wallet[]>([]);
  protected readonly itens = signal<LedgerItem[]>([]);
  protected readonly totalCents = signal(0);
  protected readonly totalCount = signal(0);
  protected readonly proximo = signal<string | null>(null);
  protected readonly carregando = signal(true);
  protected readonly carregandoMais = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly erroAcao = signal<string | null>(null);
  protected readonly alterandoId = signal<string | null>(null);

  /** `null` = primeira página (substitui a lista); texto = cursor da próxima (acrescenta). */
  private readonly pedido$ = new Subject<string | null>();
  private carteirasPorId = new Map<string, Wallet>();
  private cartoesPorId = new Map<string, string>();

  constructor() {
    this.pedido$
      .pipe(
        switchMap((cursor) => {
          // Pausa só ao digitar (`timer`, e não `debounceTime`, que os testes congelam).
          const espera = cursor === null && this.digitando ? 300 : 0;
          this.digitando = false;
          return (espera > 0 ? timer(espera) : of(0)).pipe(
            switchMap(() => {
              if (cursor === null) {
                this.carregando.set(true);
              } else {
                this.carregandoMais.set(true);
              }
              return this.entries.search({
                q: this.q(),
                kinds: this.kinds(),
                walletIds: this.walletIds(),
                tagIds: this.tagIds(),
                status: this.status() === '' ? null : (this.status() as EntryStatus),
                from: this.from(),
                to: this.to(),
                cursor,
                limit: TAMANHO_PAGINA,
              });
            }),
            tap((r) => this.aplicar(r, cursor)),
            catchError((error: unknown) => {
              this.carregando.set(false);
              this.carregandoMais.set(false);
              this.erro.set(problemMessage(error));
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();

    // FR-016: o que mudar um lançamento recarrega a busca.
    merge(
      this.entries.criados$,
      this.entries.alterados$,
      this.exclusao.mudou$,
      this.compras.criadas$,
      this.compras.alteradas$,
    )
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.pedido$.next(null));

    this.wallets
      .list('all')
      .pipe(
        takeUntilDestroyed(),
        catchError(() => of<Wallet[]>([])),
      )
      .subscribe((lista) => {
        this.carteiras.set(lista);
        this.carteirasPorId = new Map(lista.map((w) => [w.id, w]));
      });

    // Nomes dos cartões para as compras (a lista inclui arquivados); sem eles a linha só perde o nome.
    this.cards
      .list('all')
      .pipe(
        takeUntilDestroyed(),
        catchError(() => of([])),
      )
      .subscribe((lista) => (this.cartoesPorId = new Map(lista.map((c) => [c.id, c.name]))));

    // Rolagem infinita: ao chegar perto do fim da lista, pede a próxima página.
    effect((onCleanup) => {
      const alvo = this.fim()?.nativeElement;
      if (!alvo || typeof IntersectionObserver === 'undefined') return;
      const observador = new IntersectionObserver(
        (marcas) => {
          if (marcas.some((m) => m.isIntersecting)) this.carregarMais();
        },
        { rootMargin: '200px' },
      );
      observador.observe(alvo);
      onCleanup(() => observador.disconnect());
    });

    this.pedido$.next(null);
  }

  private digitando = false;

  private aplicar(r: SearchResult, cursor: string | null): void {
    this.carregando.set(false);
    this.carregandoMais.set(false);
    this.erro.set(null);
    this.itens.update((atual) => (cursor === null ? r.items : [...atual, ...r.items]));
    this.totalCents.set(r.totalCents);
    this.totalCount.set(r.totalCount);
    this.proximo.set(r.nextCursor ?? null);
  }

  protected aoDigitar(texto: string): void {
    this.q.set(texto);
    this.digitando = true;
    this.pedido$.next(null);
  }

  protected alternarTipo(tipo: LedgerKind): void {
    this.kinds.update((l) => (l.includes(tipo) ? l.filter((k) => k !== tipo) : [...l, tipo]));
    this.pedido$.next(null);
  }

  protected alternarCarteira(id: string): void {
    this.walletIds.update((l) => (l.includes(id) ? l.filter((w) => w !== id) : [...l, id]));
    this.pedido$.next(null);
  }

  protected aoMudarTags(ids: string[]): void {
    this.tagIds.set(ids);
    this.pedido$.next(null);
  }

  protected escolherSituacao(valor: string): void {
    this.status.set(valor === 'planned' || valor === 'settled' ? valor : '');
    this.pedido$.next(null);
  }

  protected escolherPeriodo(campo: 'from' | 'to', valor: string): void {
    (campo === 'from' ? this.from : this.to).set(valor);
    this.pedido$.next(null);
  }

  protected limparFiltros(): void {
    this.q.set('');
    this.kinds.set([]);
    this.walletIds.set([]);
    this.tagIds.set([]);
    this.status.set('');
    this.from.set('');
    this.to.set('');
    this.pedido$.next(null);
  }

  protected get comFiltro(): boolean {
    return (
      this.q() !== '' ||
      this.kinds().length > 0 ||
      this.walletIds().length > 0 ||
      this.tagIds().length > 0 ||
      this.status() !== '' ||
      this.from() !== '' ||
      this.to() !== ''
    );
  }

  protected carregarMais(): void {
    const cursor = this.proximo();
    if (cursor === null || this.carregando() || this.carregandoMais()) return;
    this.pedido$.next(cursor);
  }

  protected abrir(entry: Entry): void {
    this.launcher.abrir(entry);
  }

  /** Compra no cartão: abre o formulário em modo edição (003 FR-015). */
  protected abrirCompra(compra: CardPurchase): void {
    this.launcher.abrirCompra(compra);
  }

  protected nomeCartao(id: string): string {
    return this.cartoesPorId.get(id) ?? '';
  }

  /** Débito negativo e crédito positivo, como na soma do total (FR-025). */
  protected valorCompra(c: CardPurchase): number {
    return c.nature === 'credit' ? Math.abs(c.amountCents) : -Math.abs(c.amountCents);
  }

  protected identidade(item: LedgerItem): string {
    return item.entry?.id ?? item.transfer?.id ?? item.purchase?.id ?? '';
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

  protected nomeCarteira(id: string): string {
    return this.carteirasPorId.get(id)?.name ?? '';
  }

  protected corCarteira(id: string): string {
    return this.carteirasPorId.get(id)?.color ?? 'transparent';
  }

  protected data(iso: string): string {
    return dataBr(iso);
  }

  /** Saída aparece negativa e Entrada positiva, como na soma do total (FR-025). */
  protected valorComSinal(entry: Entry): number {
    return entry.kind === 'expense' ? -entry.amountCents : entry.amountCents;
  }

  protected rotuloSituacao(entry: Entry): string {
    if (entry.status === 'planned') return 'Prevista';
    return entry.kind === 'income' ? 'Recebida' : 'Paga';
  }
}
