import { SeloRecorrencia } from '../../../shared/selo-recorrencia/selo-recorrencia';
import { SeloViaClaude } from '../../../shared/selo-via-claude/selo-via-claude';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { problemMessage } from '../../../core/errors/problem-details';
import { BrlPipe, formatBrl, isYearMonth, monthLabel } from '../../../core/money';
import { TagFilter } from '../../../shared/tag-picker/tag-filter';
import { Tag } from '../../../shared/tag-picker/tag.models';
import { TagsService } from '../../../shared/tag-picker/tags.service';
import { centavosParaCampo } from '../../carteiras/carteira-form.page';
import { Wallet } from '../../carteiras/wallet.models';
import { WalletsService } from '../../carteiras/wallets.service';
import { CardPurchase } from '../../lancamento/card-purchase.models';
import { CelulaData, CelulaDialog, CelulaResultado } from '../../planejamento/celula.dialog';
import { CardPurchasesService } from '../../lancamento/card-purchases.service';
import { ExclusaoService } from '../../lancamento/exclusao.service';
import { LancamentoLauncher } from '../../lancamento/lancamento.launcher';
import { CreditCardOverview, InvoiceStatus } from '../card.models';
import { CardsService } from '../cards.service';
import { diaEMes, nomeDoMes } from '../cartoes.page';
import { Invoice, InvoicePayment } from './invoice.models';
import { InvoicesService } from './invoices.service';
import { EscopoData, EscopoDialog } from './escopo.dialog';
import { PagamentoDialog, PagamentoDialogData } from './pagamento.dialog';

const ROTULO_SITUACAO: Record<InvoiceStatus, string> = {
  no_amount_due: 'Sem valor a pagar',
  paid: 'Paga',
  open: 'Aberta',
  closed: 'Fechada',
  overdue: 'Vencida',
};

/**
 * Tela `/cartoes/:id/faturas/:mes` (US5, US6.2, US7; contracts/ui-rotas.md): navegação entre
 * faturas, totais e situação, "Conferido", compras (conferir, editar, mover, excluir com escopo e
 * Desfazer), pagamentos (pagar, editar, excluir) e "Levar restante para a próxima fatura".
 */
@Component({
  selector: 'app-fatura-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, BrlPipe, TagFilter, SeloViaClaude, SeloRecorrencia],
  templateUrl: './fatura.page.html',
  styleUrl: './fatura.page.scss',
})
export class FaturaPage {
  private readonly route = inject(ActivatedRoute);
  private readonly cards = inject(CardsService);
  private readonly invoices = inject(InvoicesService);
  private readonly compras = inject(CardPurchasesService);
  private readonly wallets = inject(WalletsService);
  private readonly exclusao = inject(ExclusaoService);
  private readonly tags = inject(TagsService);
  private readonly launcher = inject(LancamentoLauncher);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  private readonly parametros = toSignal(
    this.route.paramMap.pipe(map((p) => ({ id: p.get('id') ?? '', mes: p.get('mes') ?? '' }))),
    { initialValue: { id: '', mes: '' } },
  );

  protected readonly cardId = computed(() => this.parametros().id);
  protected readonly mes = computed(() => this.parametros().mes);
  protected readonly mesValido = computed(() => isYearMonth(this.mes()));

  protected readonly carregando = signal(true);
  protected readonly erro = signal<string | null>(null);
  protected readonly aviso = signal<string | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly cartao = signal<CreditCardOverview | null>(null);
  protected readonly fatura = signal<Invoice | null>(null);
  /** Filtro de tags (004 FR-004): só as compras com qualquer uma delas; vale ao trocar de fatura. */
  protected readonly tagIds = signal<string[]>([]);
  private readonly carteiras = signal<Wallet[]>([]);

  protected readonly diaEMes = diaEMes;
  protected readonly nomeDoMes = nomeDoMes;
  protected readonly monthLabel = monthLabel;

  private readonly nomesDeTags = signal<Map<string, Tag>>(new Map());

  /** Bloco "Por pessoa" (FR-011): uma linha por Tag de acerto e "Sem tag de acerto". */
  protected readonly porPessoa = computed(() =>
    (this.fatura()?.settlementSubtotals ?? []).map((s) => ({
      rotulo:
        s.tagId === null ? 'Sem tag de acerto' : (this.nomesDeTags().get(s.tagId)?.name ?? 'Tag'),
      cor: s.tagId === null ? null : (this.nomesDeTags().get(s.tagId)?.color ?? null),
      cents: s.cents,
    })),
  );

  protected readonly situacao = computed(() => {
    const f = this.fatura();
    if (f === null) return '';
    return f.status === 'no_amount_due' && f.totalCents < 0
      ? `Sem valor a pagar — crédito de ${formatBrl(-f.totalCents)}`
      : ROTULO_SITUACAO[f.status];
  });

  /** Previsto da fatura (007 FR-010); o servidor diz se a estimativa ainda vale (`estimateActive`). */
  protected readonly previsto = computed(() => {
    const f = this.fatura();
    if (f === null) return 0;
    const semEstimativa = Math.max(f.totalCents, f.paidCents, 0);
    return f.estimateActive && f.estimateCents
      ? Math.max(semEstimativa, f.estimateCents)
      : semEstimativa;
  });

  protected aoMudarTags(ids: string[]): void {
    this.tagIds.set(ids);
    this.carregarFatura();
  }

  /** Pagar enquanto houver restante (FR-017). */
  protected readonly podePagar = computed(() => (this.fatura()?.remainingCents ?? 0) > 0);

  constructor() {
    this.carregarCarteiras();
    this.recarregar();
    this.exclusao.mudou$.pipe(takeUntilDestroyed()).subscribe(() => this.carregarFatura());
  }

  /** Nome da carteira do pagamento (a lista inclui arquivadas). */
  protected nomeCarteira(pagamento: InvoicePayment): string {
    return this.carteiras().find((w) => w.id === pagamento.walletId)?.name ?? 'Carteira';
  }

  protected ehParcela(c: CardPurchase): boolean {
    return c.installmentPlanId != null && (c.installmentTotal ?? 1) > 1;
  }

  protected podeEditar(c: CardPurchase): boolean {
    return c.origin !== 'carry_over';
  }

  /** Débito positivo, crédito negativo na lista (FR-005). */
  protected valorCompra(c: CardPurchase): number {
    return c.nature === 'credit' ? -Math.abs(c.amountCents) : Math.abs(c.amountCents);
  }

  protected conferir(c: CardPurchase, marcada: boolean): void {
    this.executar(this.compras.setReconciled(c.id, marcada));
  }

  protected editar(c: CardPurchase): void {
    this.launcher
      .abrirCompra(c)
      .afterClosed()
      .subscribe(() => this.carregarFatura());
  }

  protected mover(c: CardPurchase, direcao: 'previous' | 'next'): void {
    if (!this.ehParcela(c)) {
      this.executar(this.compras.move(c.id, direcao));
      return;
    }
    this.perguntarEscopo<'this' | 'this_and_following'>({
      titulo: direcao === 'next' ? 'Mover para a próxima fatura' : 'Mover para a fatura anterior',
      mensagem: 'Mover só esta parcela ou esta e as seguintes?',
      opcoes: [
        { valor: 'this', rotulo: 'Só esta' },
        { valor: 'this_and_following', rotulo: 'Esta e as seguintes' },
      ],
    }).subscribe((escopo) => {
      if (escopo) this.executar(this.compras.move(c.id, direcao, escopo));
    });
  }

  protected excluir(c: CardPurchase): void {
    if (!this.ehParcela(c)) {
      this.excluirComEscopo(c, 'this');
      return;
    }
    this.perguntarEscopo<'this' | 'this_and_following' | 'all'>({
      titulo: 'Excluir parcela',
      opcoes: [
        { valor: 'this', rotulo: 'Só esta' },
        { valor: 'this_and_following', rotulo: 'Esta e as seguintes' },
        { valor: 'all', rotulo: 'Todas' },
      ],
    }).subscribe((escopo) => {
      if (escopo) this.excluirComEscopo(c, escopo);
    });
  }

  protected pagar(): void {
    const f = this.fatura();
    const cartao = this.cartao();
    if (f === null || cartao === null) return;
    this.abrirPagamento({
      cardId: cartao.id,
      month: f.dueMonth,
      carteiras: this.carteirasAtivas(cartao.paymentWalletId),
      remainingCents: f.remainingCents,
      carteiraPadraoId: cartao.paymentWalletId,
    });
  }

  protected editarPagamento(p: InvoicePayment): void {
    const f = this.fatura();
    const cartao = this.cartao();
    if (f === null || cartao === null) return;
    this.abrirPagamento({
      cardId: cartao.id,
      month: f.dueMonth,
      carteiras: this.carteirasAtivas(p.walletId),
      remainingCents: f.remainingCents,
      carteiraPadraoId: cartao.paymentWalletId,
      pagamento: p,
    });
  }

  protected excluirPagamento(p: InvoicePayment): void {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    this.invoices.deletePayment(p.id).subscribe({
      next: (resultado) => {
        this.ocupado.set(false);
        this.exclusao.oferecerDesfazer(resultado.deletionBatchId);
        this.carregarFatura();
      },
      error: (error: unknown) => this.falhar(error),
    });
  }

  /** 007 FR-009: define ou altera a estimativa (valor segue RT-05; vazio remove). */
  protected editarEstimativa(): void {
    const f = this.fatura();
    if (f === null) return;
    const data: CelulaData = {
      titulo: `Estimativa — ${nomeDoMes(f.dueMonth)}`,
      valorInicial: f.estimateCents ? centavosParaCampo(f.estimateCents) : '',
    };
    this.dialog
      .open<CelulaDialog, CelulaData, CelulaResultado>(CelulaDialog, { data })
      .afterClosed()
      .subscribe((texto) => {
        if (texto === null || texto === undefined) return;
        this.salvarEstimativa(texto.trim() === '' ? null : texto.trim());
      });
  }

  protected removerEstimativa(): void {
    this.salvarEstimativa(null);
  }

  private salvarEstimativa(valor: string | null): void {
    const f = this.fatura();
    if (f === null) return;
    this.executar(this.invoices.setEstimate(f.creditCardId, f.dueMonth, valor));
  }

  protected levarRestante(): void {
    const f = this.fatura();
    if (f === null) return;
    this.executar(this.invoices.carryOver(f.creditCardId, f.dueMonth));
  }

  private excluirComEscopo(c: CardPurchase, escopo: 'this' | 'this_and_following' | 'all'): void {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    this.compras.delete(c.id, escopo).subscribe({
      next: (resultado) => {
        this.ocupado.set(false);
        this.exclusao.oferecerDesfazer(resultado.deletionBatchId);
        this.carregarFatura();
      },
      error: (error: unknown) => this.falhar(error),
    });
  }

  private abrirPagamento(data: PagamentoDialogData): void {
    this.dialog
      .open<PagamentoDialog, PagamentoDialogData, boolean>(PagamentoDialog, {
        data,
        autoFocus: false,
      })
      .afterClosed()
      .subscribe((salvou) => {
        if (salvou) {
          this.snackBar.open('Pagamento salvo.', undefined, {
            duration: 4000,
            verticalPosition: 'top',
          });
          this.carregarFatura();
        }
      });
  }

  private perguntarEscopo<T extends string>(data: EscopoData<T>) {
    return this.dialog
      .open<EscopoDialog<T>, EscopoData<T>, T | null>(EscopoDialog, { data })
      .afterClosed();
  }

  /** Roda uma ação sobre a fatura e recarrega; erros aparecem no aviso da tela. */
  private executar(acao: { subscribe: (o: object) => unknown }): void {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    this.aviso.set(null);
    acao.subscribe({
      next: () => {
        this.ocupado.set(false);
        this.carregarFatura();
      },
      error: (error: unknown) => this.falhar(error),
    });
  }

  private falhar(error: unknown): void {
    this.ocupado.set(false);
    this.aviso.set(problemMessage(error));
  }

  /** Carteiras ativas, mais a do pagamento editado se estiver arquivada. */
  private carteirasAtivas(incluir: string): Wallet[] {
    return this.carteiras().filter((w) => !w.archived || w.id === incluir);
  }

  private carregarCarteiras(): void {
    this.wallets
      .list('all')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (lista) => this.carteiras.set(lista),
        error: (error: unknown) => this.aviso.set(problemMessage(error)),
      });
  }

  /** Carrega cartão e fatura da rota (também ao navegar de um mês para outro). */
  private recarregar(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.fatura.set(null);
      this.erro.set(null);
      this.aviso.set(null);
      this.carregando.set(true);
      if (!this.mesValido()) {
        this.carregando.set(false);
        this.erro.set('Fatura não encontrada.');
        return;
      }
      this.cards.get(this.cardId()).subscribe({
        next: (c) => this.cartao.set(c),
        error: (error: unknown) => {
          this.carregando.set(false);
          this.erro.set(problemMessage(error));
        },
      });
      this.carregarFatura();
    });
  }

  private carregarFatura(): void {
    if (!this.mesValido()) return;
    const id = this.cardId();
    const mes = this.mes();
    this.invoices.get(id, mes, this.tagIds()).subscribe({
      next: (f) => {
        if (id !== this.cardId() || mes !== this.mes()) return;
        this.fatura.set(f);
        this.carregando.set(false);
        this.carregarNomesDasTags(f);
      },
      error: (error: unknown) => {
        this.carregando.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }

  /** Só pede as tags quando a fatura tem subtotal de alguma Tag de acerto (arquivadas incluídas). */
  private carregarNomesDasTags(f: Invoice): void {
    const ids = (f.settlementSubtotals ?? []).flatMap((s) => (s.tagId === null ? [] : [s.tagId]));
    if (ids.every((id) => this.nomesDeTags().has(id))) return;
    this.tags.list('all').subscribe({
      next: (lista) => this.nomesDeTags.set(new Map(lista.map((t) => [t.id, t]))),
    });
  }
}
