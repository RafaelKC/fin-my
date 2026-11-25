import { SeloViaClaude } from '../../shared/selo-via-claude/selo-via-claude';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Icone } from '@/shared/ui/icone';
import { Router } from '@angular/router';
import { Subject, catchError, forkJoin, map, of, switchMap, timer } from 'rxjs';
import { asProblem, problemMessage } from '../../core/errors/problem-details';
import {
  BrlPipe,
  InstallmentMode,
  YearMonth,
  formatBrl,
  invoiceFor,
  isYearMonth,
  monthOfDate,
  parseValueExpression,
  today,
} from '../../core/money';
import { DevicePrefsService, LancamentoKind } from '../../core/prefs/device-prefs.service';
import { AmountInput } from '../../shared/amount-input/amount-input';
import { TagPicker } from '../../shared/tag-picker/tag-picker';
import { Tag } from '../../shared/tag-picker/tag.models';
import { TagsService } from '../../shared/tag-picker/tags.service';
import { WALLET_COLORS, Wallet } from '../carteiras/wallet.models';
import { WalletsService } from '../carteiras/wallets.service';
import { ConfirmarData, ConfirmarDialog, ConfirmarResultado } from '../carteiras/confirmar.dialog';
import { CreditCardOverview } from '../cartoes/card.models';
import { CardsService } from '../cartoes/cards.service';
import { EscopoData, EscopoDialog } from '../cartoes/fatura/escopo.dialog';
import {
  AVISO_CREDITO,
  CODIGO_FATURA_PAGA,
  CardPurchase,
  CardPurchaseCreate,
  CardPurchaseUpdate,
  EscopoCompra,
} from './card-purchase.models';
import { CardPurchasesService } from './card-purchases.service';
import { PARCELAS_MAX, parseValorCartao, previaCompra } from './compra-cartao-logic';
import { OCORRENCIAS_MAX, RecurrenceEndType } from '../recorrencias/recurrence.models';
import { RecurrenceFromItem } from '../recorrencias/recurrence.models';
import { RecurrencesService } from '../recorrencias/recurrences.service';
import { EntriesService } from './entries.service';
import { TransfersService } from './transfers.service';
import { ExclusaoService } from './exclusao.service';
import {
  DESCRICAO_MAX,
  DESCRICAO_TRANSFERENCIA,
  Entry,
  EntryCreate,
  EntryKind,
  EntryStatus,
  EntrySuggestion,
  EntryUpdate,
  OBSERVACAO_MAX,
  TransferCreate,
} from './entry.models';
import {
  ModoDivisao,
  PARTES_MAX,
  ParteDivisao,
  ROTULO_EU,
  calcularDivisao,
  montarSplit,
  partesIniciais,
  totalDividido,
} from './dividir-logic';
import { centavosParaExpressao, novaChaveIdempotencia, situacaoPadrao } from './lancamento-logic';
import { DADOS_DIALOGO, Dialogo, DialogoRef } from '@/shared/ui/dialogo';
import { Avisos } from '@/shared/ui/avisos';
import { ZardButtonComponent } from '@/shared/components/button';
import { CampoDirective } from '@/shared/ui/campo.directive';
import { ZardNativeSelectDirective } from '@/shared/components/native-select';

export const MENSAGEM_SEM_CONEXAO_LANCAMENTO =
  'Sem conexão. O lançamento não foi salvo; tente de novo.';
export const ERRO_MESMA_CARTEIRA = 'Escolha carteiras diferentes.';
export const MENSAGEM_CONFLITO =
  'Este lançamento foi alterado em outro aparelho. Os dados foram recarregados; confira e salve de novo.';
export const ERRO_CREDITO_A_VISTA = 'Estorno ou crédito é sempre à vista.';
export const AVISO_ESTORNO = 'Lançado como estorno/crédito';
export const AVISO_DATA_ANTERIOR =
  'Esta data é anterior ao saldo inicial da carteira. O saldo inicial já deveria incluir este valor.';

const DATA = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;

/** Dados do diálogo: com `entry`, o formulário abre em modo edição (todos os campos, FR-022). */
export interface LancamentoSheetData {
  entry?: Entry;
  /** Compra no cartão a editar (003 FR-015; tela da fatura). */
  purchase?: CardPurchase;
  /** Entrada nova já preenchida (004 R4: "Gerar cobrança"); nada é salvo sem "Salvar". */
  preencher?: EntradaPreenchida;
}

/** Valores de uma Entrada pré-preenchida; a carteira é sempre a principal (US3.1). */
export interface EntradaPreenchida {
  description: string;
  amountCents: number;
  date: string;
  referenceMonth: string;
  tagIds: string[];
}

/** Rascunho em `localStorage` (`finmy.draft.lancamento`, research R12). */
interface Rascunho {
  savedOn: string;
  kind: LancamentoKind;
  amount: string;
  description: string;
  date: string;
  walletId: string;
  walletManual: boolean;
  /** Carteira de destino da transferência (rascunhos antigos não têm). */
  toWalletId?: string;
  toManual?: boolean;
  /** Cartão da aba Cartão (rascunhos antigos não têm). */
  cardId?: string;
  cardManual?: boolean;
  status: EntryStatus | null;
  refMonth: string | null;
  notes: string;
  /** Tags escolhidas (rascunhos antigos não têm). */
  tagIds?: string[];
  tagsManual?: boolean;
}

/**
 * Formulário rápido de lançamento (US2; FR-008..FR-013; contracts/ui-rotas.md). Aberto pelo "+"
 * da casca (`LancamentoLauncher`): tela cheia no celular, diálogo no computador. Cria Saída,
 * Entrada e Transferência (De/Para, sem situação nem sugestões; US5). Com `entry` nos dados do
 * diálogo abre para editar (salva por PUT com a `version`; 409 recarrega) e oferece Duplicar (só
 * pede a nova data) e Excluir (com Desfazer de 8 s, RT-09).
 */
@Component({
  selector: 'app-lancamento-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ZardButtonComponent,
    CampoDirective,
    ZardNativeSelectDirective,
    FormsModule,
    Icone,
    AmountInput,
    TagPicker,
    BrlPipe,
    SeloViaClaude,
  ],
  templateUrl: './lancamento.sheet.html',
  styleUrl: './lancamento.sheet.scss',
})
export class LancamentoSheet {
  private readonly ref = inject<DialogoRef<LancamentoSheet, Entry | undefined>>(DialogoRef);
  private readonly entries = inject(EntriesService);
  private readonly transfers = inject(TransfersService);
  private readonly wallets = inject(WalletsService);
  private readonly cards = inject(CardsService);
  private readonly compras = inject(CardPurchasesService);
  private readonly dialog = inject(Dialogo);
  private readonly router = inject(Router);
  private readonly prefs = inject(DevicePrefsService);
  private readonly avisos = inject(Avisos);
  private readonly destroyRef = inject(DestroyRef);
  private readonly exclusao = inject(ExclusaoService);
  private readonly recorrencias = inject(RecurrencesService);
  private readonly tagsApi = inject(TagsService);
  private readonly dados = inject<LancamentoSheetData | null>(DADOS_DIALOGO, { optional: true });
  private readonly edicao = this.dados?.entry ?? null;
  private readonly preenchida = this.dados?.preencher ?? null;
  /** Compra no cartão aberta na edição, com a `version` mais recente conhecida. */
  private compraAtual: CardPurchase | null = this.dados?.purchase ?? null;
  /** Lançamento aberto na edição, com a `version` mais recente conhecida. */
  private atual: Entry | null = this.edicao;
  private readonly valorCampo = viewChild(AmountInput);

  protected readonly editandoCompra = this.compraAtual !== null;
  protected readonly criadoVia = this.edicao?.createdVia ?? this.compraAtual?.createdVia ?? null;
  protected readonly editando = this.edicao !== null || this.editandoCompra;
  protected readonly descricaoMax = DESCRICAO_MAX;
  protected readonly observacaoMax = OBSERVACAO_MAX;

  protected readonly hoje = signal(today());
  protected readonly kind = signal<LancamentoKind>(this.prefs.lastKind());
  protected readonly amount = signal('');
  protected readonly description = signal('');
  protected readonly date = signal(this.hoje());
  /** Carteira da entrada/saída; na transferência é a de origem ("De"). */
  protected readonly walletId = signal('');
  /** Carteira de destino da transferência ("Para"). */
  protected readonly toWalletId = signal('');
  private readonly toManual = signal(false);
  /** `true` quando a pessoa escolheu a carteira à mão: sugestões não a trocam mais (FR-011). */
  private readonly walletManual = signal(false);
  /** Situação escolhida à mão; `null` = segue a data (FR-012). */
  protected readonly statusManual = signal<EntryStatus | null>(null);
  /** Mês de referência escolhido à mão; `null` = segue a data (RT-04). */
  protected readonly refManual = signal<string | null>(null);
  protected readonly notes = signal('');
  /** Tags escolhidas (de acerto ou não); o `TagPicker` edita esta lista (004 FR-003). */
  protected readonly tagIds = signal<string[]>([]);
  /** `true` quando a pessoa mexeu no campo Tags: sugestões não o sobrescrevem mais (FR-003). */
  private readonly tagsManual = signal(false);
  protected readonly maisOpcoes = signal(false);

  // aba Cartão (003)
  protected readonly parcelasMax = PARCELAS_MAX;
  protected readonly opcoesParcelas = Array.from({ length: PARCELAS_MAX }, (_, i) => i + 1);
  protected readonly cartoes = signal<CreditCardOverview[]>([]);
  protected readonly carregandoCartoes = signal(false);
  protected readonly erroCartoes = signal<string | null>(null);
  private cartoesPedidos = false;
  protected readonly cardId = signal('');
  /** `true` quando o cartão foi escolhido à mão: sugestões não o trocam mais (FR-011). */
  private readonly cardManual = signal(false);
  /** N: total de parcelas (1 = à vista). */
  protected readonly parcelas = signal(1);
  protected readonly modoParcela = signal<Exclude<InstallmentMode, 'in_progress'>>('total_amount');
  protected readonly emAndamento = signal(false);
  /** p: próxima parcela do parcelamento em andamento. */
  protected readonly proximaParcela = signal(1);
  /** Fatura da parcela p escolhida à mão; `null` = fatura atual do cartão (FR-014). */
  protected readonly faturaManual = signal<string | null>(null);

  protected readonly carteiras = signal<Wallet[]>([]);
  protected readonly carregandoCarteiras = signal(true);
  protected readonly sugestoes = signal<EntrySuggestion[]>([]);
  protected readonly tentou = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);

  // duplicar (só a nova data) e excluir
  protected readonly duplicando = signal(false);
  protected readonly dataDuplicada = signal('');
  protected readonly erroDuplicar = signal<string | null>(null);
  private chaveDuplicar = novaChaveIdempotencia();

  // "Repetir todo mês" (005 US4): só na edição de Entrada/Saída/Compra à vista que ainda não é recorrência
  protected readonly repetindo = signal(false);
  protected readonly repetirFim = signal<RecurrenceEndType>('none');
  protected readonly repetirMes = signal('');
  protected readonly repetirQtd = signal('12');
  protected readonly erroRepetir = signal<string | null>(null);
  private chaveRepetir = novaChaveIdempotencia();
  protected readonly ocorrenciasMax = OCORRENCIAS_MAX;

  // criar carteira inline (nenhuma cadastrada)
  protected readonly criandoCarteira = signal(false);
  protected readonly novaNome = signal('');
  protected readonly novaSaldo = signal('0');
  protected readonly novaErro = signal<string | null>(null);

  protected readonly status = computed<EntryStatus>(
    () => this.statusManual() ?? situacaoPadrao(this.date(), this.hoje()),
  );
  protected readonly refMonth = computed(() => {
    const manual = this.refManual();
    if (manual !== null) return manual;
    return DATA.test(this.date()) ? monthOfDate(this.date()) : '';
  });
  protected readonly ehTransferencia = computed(() => this.kind() === 'transfer');
  protected readonly ehCartao = computed(() => this.kind() === 'card');
  protected readonly descricaoPadrao = DESCRICAO_TRANSFERENCIA;
  protected readonly rotuloRealizada = computed(() =>
    this.kind() === 'income' ? 'Recebida' : 'Paga',
  );

  protected readonly valor = computed(() =>
    this.ehCartao() ? parseValorCartao(this.amount()) : parseValueExpression(this.amount()),
  );
  protected readonly cartaoEscolhido = computed(
    () => this.cartoes().find((c) => c.id === this.cardId()) ?? null,
  );
  /** Fatura atual do cartão hoje: padrão da fatura da parcela p (FR-014). */
  private readonly faturaAtual = computed<YearMonth | null>(() => {
    const c = this.cartaoEscolhido();
    return c === null ? null : invoiceFor(this.hoje(), c.closingDay, c.dueDay);
  });
  protected readonly faturaEmAndamento = computed(
    () => this.faturaManual() ?? this.faturaAtual() ?? '',
  );
  /** RT-02/R6: valor negativo vira estorno (crédito). */
  protected readonly ehCredito = computed(() => {
    const v = this.valor();
    return this.ehCartao() && v.ok && v.cents < 0;
  });
  protected readonly erroParcelas = computed(() => {
    if (!this.ehCartao()) return null;
    const total = this.parcelas();
    if (this.ehCredito() && (total > 1 || this.emAndamento())) return ERRO_CREDITO_A_VISTA;
    if (!this.emAndamento()) return null;
    const p = this.proximaParcela();
    if (!Number.isInteger(p) || p < 1 || p > total) {
      return `A próxima parcela deve estar entre 1 e ${total}.`;
    }
    return isYearMonth(this.faturaEmAndamento()) ? null : 'Informe a fatura da parcela.';
  });
  protected readonly erroCartao = computed(() => {
    if (!this.ehCartao() || this.carregandoCartoes() || this.cartoes().length === 0) return null;
    return this.cartaoEscolhido() === null ? 'Escolha um cartão.' : null;
  });
  /** Linha "Fatura de março — vence 25/03" / "3x de …" (FR-010, US3.3); porta TS, sem API. */
  protected readonly previaCompra = computed(() => {
    const c = this.cartaoEscolhido();
    if (!this.ehCartao() || c === null) return null;
    const v = this.valor();
    const faturaEmAndamento = this.faturaEmAndamento();
    if (this.emAndamento() && !isYearMonth(faturaEmAndamento)) return null;
    try {
      return previaCompra({
        date: this.date(),
        closingDay: c.closingDay,
        dueDay: c.dueDay,
        cents: v.ok ? Math.abs(v.cents) : null,
        parcelas: this.parcelas(),
        modo: this.modoParcela(),
        emAndamento: this.emAndamento(),
        proxima: this.proximaParcela(),
        faturaEmAndamento,
      });
    } catch {
      return null;
    }
  });
  protected readonly carteiraEscolhida = computed(
    () => this.carteiras().find((w) => w.id === this.walletId()) ?? null,
  );
  protected readonly avisoDataAnterior = computed(() => {
    if (!DATA.test(this.date())) return false;
    const envolvidas = [this.carteiraEscolhida()];
    if (this.ehTransferencia()) {
      envolvidas.push(this.carteiras().find((w) => w.id === this.toWalletId()) ?? null);
    }
    return envolvidas.some((w) => w !== null && this.date() < w.initialBalanceDate);
  });
  protected readonly previaSaldoNovo = computed(() => {
    const r = parseValueExpression(this.novaSaldo(), true);
    return r.ok ? `= ${formatBrl(r.cents)}` : null;
  });

  protected readonly erroValor = computed(() => {
    const r = this.valor();
    if (r.ok) return null;
    return this.amount().trim() === ''
      ? 'Informe o valor.'
      : null; /* a mensagem do parser já aparece na pré-visualização */
  });
  protected readonly erroDescricao = computed(() => {
    const t = this.description().trim();
    // Transferência sem descrição usa o padrão "Transferência" (FR-021).
    if (t.length === 0) return this.ehTransferencia() ? null : 'Informe a descrição.';
    return t.length > DESCRICAO_MAX
      ? `A descrição pode ter até ${DESCRICAO_MAX} caracteres.`
      : null;
  });

  /** US5.3: origem e destino iguais são barrados antes de enviar. */
  protected readonly erroDestino = computed(() => {
    if (!this.ehTransferencia() || this.carregandoCarteiras()) return null;
    if (this.toWalletId() === '') return 'Escolha a carteira de destino.';
    return this.toWalletId() === this.walletId() ? ERRO_MESMA_CARTEIRA : null;
  });

  // Dividir (004 US4, FR-012): só em lançamento novo de Saída ou Cartão.
  protected readonly dividirAberto = signal(false);
  protected readonly modoDivisao = signal<ModoDivisao>('equal');
  protected readonly partes = signal<ParteDivisao[]>([]);
  private readonly catalogoTags = signal<Tag[]>([]);
  protected readonly partesMax = PARTES_MAX;
  protected readonly rotuloEu = ROTULO_EU;
  protected readonly podeDividir = computed(() => {
    if (this.editando) return false;
    if (this.kind() === 'expense') return true;
    return this.ehCartao() && !this.emAndamento() && !this.ehCredito();
  });
  protected readonly dividindo = computed(() => this.dividirAberto() && this.podeDividir());
  protected readonly tagsDeAcerto = computed(() =>
    this.catalogoTags().filter((t) => t.isSettlement && !t.archived),
  );
  protected readonly calculoDivisao = computed(() => {
    const v = this.valor();
    const total = v.ok
      ? totalDividido(v.cents, this.ehCartao() ? this.parcelas() : 1, this.modoParcela())
      : null;
    return calcularDivisao(this.modoDivisao(), total, this.partes());
  });

  // Selo "Dividida em N" e as demais partes (FR-014).
  protected readonly seloDivisao =
    (this.edicao?.splitCount ?? this.compraAtual?.splitCount ?? 0) > 1
      ? (this.edicao?.splitCount ?? this.compraAtual?.splitCount ?? 0)
      : 0;
  protected readonly outrasPartesAbertas = signal(false);
  protected readonly outrasPartes = signal<{ id: string; rotulo: string; valor: number }[]>([]);
  protected readonly carregandoPartes = signal(false);
  protected readonly erroPartes = signal<string | null>(null);

  protected readonly termo$ = new Subject<string>();
  private chave = novaChaveIdempotencia();
  private ultimoCorpo = '';
  private finalizado = false;

  constructor() {
    if (this.edicao !== null) this.preencherEdicao(this.edicao);
    else if (this.compraAtual !== null) this.preencherEdicaoCompra(this.compraAtual);
    else if (this.preenchida !== null) this.aplicarPreenchimento(this.preenchida);
    else this.restaurarRascunho();

    this.termo$
      .pipe(
        // `timer` (e não `debounceTime`): não depende de `Date.now`, que os testes congelam.
        switchMap((q) => {
          const kind = this.kind();
          // Transferência não tem sugestões (contracts/ui-rotas.md).
          if (kind === 'transfer' || q.trim().length < 1) return of<EntrySuggestion[]>([]);
          return timer(120).pipe(
            switchMap(() =>
              this.entries.suggestions(q.trim(), kind === 'card' ? 'card_purchase' : kind),
            ),
            catchError(() => of<EntrySuggestion[]>([])),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((lista) => this.sugestoes.set(lista));

    // Na edição a carteira do lançamento pode estar arquivada: ela continua escolhível.
    this.wallets
      .list(this.editando ? 'all' : 'false')
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (lista) => {
          this.carteiras.set(
            this.edicao === null
              ? lista
              : lista.filter((w) => !w.archived || w.id === this.edicao!.walletId),
          );
          this.carregandoCarteiras.set(false);
          this.resolverCarteira();
          this.usarCarteiraPrincipalDaCobranca();
        },
        error: (error: unknown) => {
          this.carregandoCarteiras.set(false);
          this.erro.set(problemMessage(error));
        },
      });

    if (this.kind() === 'card') this.carregarCartoes();

    effect(() => this.guardarRascunho());

    afterNextRender(() => this.valorCampo()?.focus());
  }

  protected escolherTipo(kind: LancamentoKind): void {
    if (this.kind() === kind) return;
    this.kind.set(kind);
    this.sugestoes.set([]);
    this.termo$.next(this.description());
    if (kind === 'card') this.carregarCartoes();
    if (!this.walletManual()) this.walletId.set(this.carteiraPadrao());
    this.resolverDestino();
  }

  protected abrirDividir(): void {
    if (!this.podeDividir()) return;
    this.dividirAberto.set(true);
    this.modoDivisao.set('equal');
    this.partes.set(partesIniciais(null));
    this.carregarCatalogo((lista) => {
      const acertos = lista.filter((t) => t.isSettlement && !t.archived);
      // Uma tag de acerto já escolhida vira a pessoa da segunda parte (US1.4 → Dividir).
      const escolhida = this.tagIds().find((id) => acertos.some((t) => t.id === id)) ?? null;
      if (escolhida !== null) {
        this.tagIds.update((l) => l.filter((id) => id !== escolhida));
      }
      const primeira = escolhida ?? acertos[0]?.id ?? null;
      this.partes.set(partesIniciais(primeira));
    });
  }

  protected fecharDividir(): void {
    this.dividirAberto.set(false);
  }

  protected escolherModoDivisao(modo: ModoDivisao): void {
    this.modoDivisao.set(modo);
  }

  protected escolherPessoaParte(indice: number, valor: string): void {
    this.partes.update((l) => l.map((p, i) => (i === indice ? { ...p, tagId: valor || null } : p)));
  }

  protected digitarValorParte(indice: number, texto: string): void {
    this.partes.update((l) => l.map((p, i) => (i === indice ? { ...p, valor: texto } : p)));
  }

  /** Pessoas ainda livres para a parte `indice` (sem repetir; "Eu" só uma vez). */
  protected pessoasDaParte(indice: number): { id: string; nome: string }[] {
    const usadas = new Set(
      this.partes()
        .filter((_, i) => i !== indice)
        .map((p) => p.tagId),
    );
    const lista = this.tagsDeAcerto().map((t) => ({ id: t.id, nome: t.name }));
    const eu = usadas.has(null) ? [] : [{ id: '', nome: ROTULO_EU }];
    return [...eu, ...lista.filter((t) => !usadas.has(t.id))];
  }

  protected adicionarParte(): void {
    if (this.partes().length >= PARTES_MAX) return;
    const usadas = new Set(this.partes().map((p) => p.tagId));
    const livre = this.tagsDeAcerto().find((t) => !usadas.has(t.id))?.id ?? null;
    this.partes.update((l) => [...l, { tagId: livre, valor: '' }]);
  }

  protected removerParte(indice: number): void {
    if (this.partes().length <= 2) return;
    this.partes.update((l) => l.filter((_, i) => i !== indice));
  }

  protected valorCalculado(indice: number): string {
    const v = this.calculoDivisao().valores[indice];
    return v === null || v === undefined ? '' : formatBrl(v);
  }

  /** FR-014: tocar no selo mostra as demais partes (mesmo grupo, achadas pela busca da descrição/data). */
  protected alternarOutrasPartes(): void {
    if (this.outrasPartesAbertas()) {
      this.outrasPartesAbertas.set(false);
      return;
    }
    this.outrasPartesAbertas.set(true);
    const origem = this.edicao ?? this.compraAtual;
    if (origem === null || this.carregandoPartes()) return;
    const grupo = origem.splitGroupId;
    this.carregandoPartes.set(true);
    this.erroPartes.set(null);
    forkJoin([
      this.entries.search({
        q: origem.description,
        from: origem.date,
        to: origem.date,
        limit: 100,
      }),
      this.tagsApi.list('all'),
    ]).subscribe({
      next: ([resultado, tags]) => {
        const nomes = new Map(tags.map((t) => [t.id, t.name]));
        const rotulo = (tagId?: string | null) =>
          tagId ? (nomes.get(tagId) ?? 'Tag removida') : ROTULO_EU;
        const outras = resultado.items.flatMap((item) => {
          const parte = item.entry ?? item.purchase;
          if (!parte || !grupo || parte.splitGroupId !== grupo || parte.id === origem.id) return [];
          return [
            { id: parte.id, rotulo: rotulo(parte.settlementTagId), valor: parte.amountCents },
          ];
        });
        this.outrasPartes.set(outras);
        this.carregandoPartes.set(false);
      },
      error: (error: unknown) => {
        this.carregandoPartes.set(false);
        this.erroPartes.set(problemMessage(error));
      },
    });
  }

  private carregarCatalogo(aoCarregar: (lista: Tag[]) => void): void {
    this.tagsApi
      .list('all')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (lista) => {
          this.catalogoTags.set(lista);
          aoCarregar(lista);
        },
        error: (error: unknown) => this.erro.set(problemMessage(error)),
      });
  }

  protected aoAlterarTags(): void {
    this.tagsManual.set(true);
  }

  protected aoDigitarDescricao(texto: string): void {
    this.description.set(texto);
    this.termo$.next(texto);
  }

  /** FR-011: aplica a carteira da sugestão só se ainda não foi alterada à mão; nunca o valor. */
  protected escolherSugestao(s: EntrySuggestion): void {
    this.description.set(s.description);
    this.termo$.next('');
    this.sugestoes.set([]);
    // FR-003: as tags do lançamento mais recente com a descrição, se o campo não foi alterado.
    if (!this.tagsManual()) this.tagIds.set([...(s.tagIds ?? [])]);
    if (this.ehCartao()) {
      // FR-011: o cartão da compra mais recente, se ativo e ainda não alterado à mão.
      const ativo = s.creditCardId && this.cartoes().some((c) => c.id === s.creditCardId);
      if (!this.cardManual() && ativo) this.cardId.set(s.creditCardId!);
      return;
    }
    const ativa = s.walletId && this.carteiras().some((w) => w.id === s.walletId);
    if (!this.walletManual() && ativa) this.walletId.set(s.walletId!);
  }

  protected escolherCarteira(id: string): void {
    this.walletId.set(id);
    this.walletManual.set(true);
    this.resolverDestino();
  }

  protected escolherCartao(id: string): void {
    this.cardId.set(id);
    this.cardManual.set(true);
    this.faturaManual.set(null);
  }

  protected escolherParcelas(valor: string): void {
    this.parcelas.set(Number(valor));
    if (this.proximaParcela() > this.parcelas()) this.proximaParcela.set(this.parcelas());
  }

  protected escolherProximaParcela(valor: string): void {
    this.proximaParcela.set(Number(valor));
  }

  protected escolherAndamento(ligado: boolean): void {
    this.emAndamento.set(ligado);
    if (ligado && this.parcelas() < 2) this.parcelas.set(2);
    if (ligado) this.proximaParcela.set(Math.min(this.proximaParcela(), this.parcelas()));
  }

  protected irParaNovoCartao(): void {
    this.ref.close(undefined);
    void this.router.navigateByUrl('/cartoes/novo');
  }

  protected escolherDestino(id: string): void {
    this.toWalletId.set(id);
    this.toManual.set(true);
  }

  protected escolherSituacao(status: EntryStatus): void {
    this.statusManual.set(status);
  }

  protected abrirCriarCarteira(): void {
    this.criandoCarteira.set(true);
    this.novaErro.set(null);
  }

  protected criarCarteira(): void {
    const nome = this.novaNome().trim();
    const saldo = parseValueExpression(this.novaSaldo(), true);
    if (nome.length === 0) {
      this.novaErro.set('Informe o nome da carteira.');
      return;
    }
    if (!saldo.ok) {
      this.novaErro.set('Informe um saldo inicial válido.');
      return;
    }
    this.ocupado.set(true);
    this.novaErro.set(null);
    this.wallets
      .create({
        name: nome,
        type: 'checking',
        color: WALLET_COLORS[0].value,
        initialBalance: this.novaSaldo().trim(),
        initialBalanceDate: today(),
      })
      .subscribe({
        next: (w) => {
          this.ocupado.set(false);
          this.carteiras.set([w]);
          this.walletId.set(w.id);
          this.walletManual.set(false);
          this.criandoCarteira.set(false);
        },
        error: (error: unknown) => {
          this.ocupado.set(false);
          this.novaErro.set(
            this.semConexao(error) ? MENSAGEM_SEM_CONEXAO_LANCAMENTO : problemMessage(error),
          );
        },
      });
  }

  protected salvar(lancarOutro: boolean): void {
    if (this.ocupado()) return;
    this.tentou.set(true);
    this.erro.set(null);

    if (this.atual !== null) {
      this.salvarEdicao(this.atual);
      return;
    }

    if (this.compraAtual !== null) {
      const compra = this.compraAtual;
      if (!this.compraEditadaValida()) return;
      if (!this.ehParcela(compra)) {
        this.salvarEdicaoCompra(compra, false, 'this');
        return;
      }
      // US3.4: descrição e observação valem para esta parcela ou para todas.
      this.perguntarEscopo<'this' | 'all'>({
        titulo: 'Alterar só esta parcela ou todas?',
        opcoes: [
          { valor: 'this', rotulo: 'Só esta' },
          { valor: 'all', rotulo: 'Todas' },
        ],
      }).subscribe((escopo) => {
        if (escopo) this.salvarEdicaoCompra(compra, false, escopo);
      });
      return;
    }

    if (this.ehTransferencia()) {
      this.salvarTransferencia(lancarOutro);
      return;
    }

    if (this.ehCartao()) {
      this.salvarCompra(lancarOutro, false);
      return;
    }

    const corpo = this.montarCorpo();
    if (corpo === null) return;
    const split = this.montarDivisao();
    if (split === undefined) return;

    // Mesma chave só para reenvio idêntico: outra chave com o mesmo corpo seria um lançamento novo,
    // e a mesma chave com corpo diferente é recusada (422 `idempotency.key_reused`).
    const serial = JSON.stringify(split ? { ...corpo, split } : corpo);
    if (serial !== this.ultimoCorpo) {
      this.chave = novaChaveIdempotencia();
      this.ultimoCorpo = serial;
    }

    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      this.avisarSemConexao();
      return;
    }

    this.ocupado.set(true);
    const pedido$ = split
      ? this.entries.createSplit({ ...corpo, split }, this.chave)
      : this.entries.create(corpo, this.chave).pipe(map((e) => [e]));
    pedido$.subscribe({
      next: (criadas) => {
        this.ocupado.set(false);
        this.aoSalvar(
          criadas[0].kind,
          criadas[0].walletId,
          lancarOutro,
          criadas[0],
          split ? `Lançamento dividido em ${criadas.length}.` : 'Lançamento salvo.',
        );
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        if (this.semConexao(error)) {
          this.avisarSemConexao();
          return;
        }
        const problem = asProblem(error);
        this.erro.set(
          problem?.code === 'not_found'
            ? 'Carteira não encontrada. Escolha outra carteira.'
            : problemMessage(error),
        );
      },
    });
  }

  /** `null` = sem divisão; `undefined` = divisão inválida (a tela mostra o motivo; não salva). */
  private montarDivisao() {
    if (!this.dividindo()) return null;
    if (this.calculoDivisao().erro !== null) return undefined;
    return montarSplit(this.modoDivisao(), this.partes());
  }

  /** US2–US4: cria a compra (à vista, parcelas ou em andamento); 409 de fatura paga pede confirmação (R5). */
  private salvarCompra(lancarOutro: boolean, confirmarFaturaPaga: boolean): void {
    const corpo = this.montarCorpoCompra(confirmarFaturaPaga);
    if (corpo === null) return;
    const split = this.montarDivisao();
    if (split === undefined) return;

    // Mesma regra de chave das demais criações: o corpo mudou (ex.: confirmação) → chave nova.
    const serial = JSON.stringify(split ? { ...corpo, split } : corpo);
    if (serial !== this.ultimoCorpo) {
      this.chave = novaChaveIdempotencia();
      this.ultimoCorpo = serial;
    }

    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      this.avisarSemConexao();
      return;
    }

    this.ocupado.set(true);
    const pedido$ = split
      ? this.compras.createSplit({ ...corpo, split }, this.chave)
      : this.compras.create(corpo, this.chave);
    pedido$.subscribe({
      next: (resultado) => {
        this.ocupado.set(false);
        const credito = resultado.warnings.includes(AVISO_CREDITO);
        this.aoSalvar(
          'card',
          corpo.creditCardId,
          lancarOutro,
          undefined,
          split
            ? `Compra dividida em ${split.parts.length}.`
            : credito
              ? `${AVISO_ESTORNO}.`
              : 'Lançamento salvo.',
        );
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        if (this.semConexao(error)) {
          this.avisarSemConexao();
          return;
        }
        const problem = asProblem(error);
        if (problem?.code === CODIGO_FATURA_PAGA) {
          this.pedirConfirmacaoFaturaPaga(problem.detail, () =>
            this.salvarCompra(lancarOutro, true),
          );
          return;
        }
        this.erro.set(
          problem?.code === 'not_found'
            ? 'Cartão não encontrado. Escolha outro cartão.'
            : problemMessage(error),
        );
      },
    });
  }

  private pedirConfirmacaoFaturaPaga(detail: string | undefined, confirmar: () => void): void {
    const data: ConfirmarData = {
      titulo: 'Fatura já paga',
      mensagem: detail ?? 'Esta fatura já foi paga e voltará a ter valor em aberto',
      confirmar: 'Lançar mesmo assim',
    };
    this.dialog
      .open<ConfirmarDialog, ConfirmarData, ConfirmarResultado>(ConfirmarDialog, { data })
      .afterClosed()
      .subscribe((resultado) => {
        if (resultado === null || resultado === undefined) return;
        confirmar();
      });
  }

  private salvarTransferencia(lancarOutro: boolean): void {
    const corpo = this.montarCorpoTransferencia();
    if (corpo === null) return;

    // Mesma regra de chave das entradas/saídas: reenvio idêntico reaproveita a chave.
    const serial = JSON.stringify(corpo);
    if (serial !== this.ultimoCorpo) {
      this.chave = novaChaveIdempotencia();
      this.ultimoCorpo = serial;
    }

    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      this.avisarSemConexao();
      return;
    }

    this.ocupado.set(true);
    this.transfers.create(corpo, this.chave).subscribe({
      next: (transfer) => {
        this.ocupado.set(false);
        this.aoSalvar('transfer', transfer.fromWalletId, lancarOutro, undefined);
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        if (this.semConexao(error)) {
          this.avisarSemConexao();
          return;
        }
        const problem = asProblem(error);
        this.erro.set(
          problem?.code === 'not_found'
            ? 'Carteira não encontrada. Escolha outra carteira.'
            : problem?.code === 'transfer.same_wallet'
              ? ERRO_MESMA_CARTEIRA
              : problemMessage(error),
        );
      },
    });
  }

  private salvarEdicao(original: Entry): void {
    const base = this.montarCorpo();
    if (base === null) return;
    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      this.avisarSemConexao();
      return;
    }

    // Edição manda todos os campos; `referenceMonth` só quando foi escolhido à mão (RT-04, R7).
    const corpo: EntryUpdate = {
      ...base,
      status: this.status(),
      notes: base.notes ?? null,
      version: original.version,
      // Na edição o servidor troca as tags pelas enviadas: lista vazia remove todas.
      tagIds: [...this.tagIds()],
    };
    this.ocupado.set(true);
    this.entries.update(original.id, corpo).subscribe({
      next: (entry) => {
        this.ocupado.set(false);
        this.avisos.open('Lançamento atualizado.', undefined, {
          duration: 4000,
          verticalPosition: 'top',
        });
        this.ref.close(entry);
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        if (this.semConexao(error)) {
          this.avisarSemConexao();
          return;
        }
        if (asProblem(error)?.code === 'concurrency_conflict') {
          this.recarregarPorConflito(original.id);
          return;
        }
        this.erro.set(problemMessage(error));
      },
    });
  }

  private ehParcela(compra: CardPurchase): boolean {
    return compra.installmentPlanId != null && (compra.installmentTotal ?? 1) > 1;
  }

  private compraEditadaValida(): boolean {
    return (
      this.valor().ok &&
      this.erroDescricao() === null &&
      DATA.test(this.date()) &&
      this.cartaoEscolhido() !== null &&
      this.notes().trim().length <= OBSERVACAO_MAX
    );
  }

  private perguntarEscopo<T extends string>(data: EscopoData<T>) {
    return this.dialog
      .open<EscopoDialog<T>, EscopoData<T>, T | null>(EscopoDialog, { data })
      .afterClosed();
  }

  /**
   * US3.5: exclui a compra (em parcela, "Só esta / Esta e as seguintes / Todas"), fecha o
   * formulário e oferece "Desfazer" por 8 s (RT-09).
   */
  protected excluirCompra(): void {
    const compra = this.compraAtual;
    if (compra === null || this.ocupado()) return;
    if (!this.ehParcela(compra)) {
      this.excluirCompraComEscopo(compra, 'this');
      return;
    }
    this.perguntarEscopo<EscopoCompra>({
      titulo: 'Excluir parcela',
      opcoes: [
        { valor: 'this', rotulo: 'Só esta' },
        { valor: 'this_and_following', rotulo: 'Esta e as seguintes' },
        { valor: 'all', rotulo: 'Todas' },
      ],
    }).subscribe((escopo) => {
      if (escopo) this.excluirCompraComEscopo(compra, escopo);
    });
  }

  private excluirCompraComEscopo(compra: CardPurchase, escopo: EscopoCompra): void {
    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      this.avisarSemConexao();
      return;
    }
    this.ocupado.set(true);
    this.erro.set(null);
    this.compras.delete(compra.id, escopo).subscribe({
      next: (resultado) => {
        this.ocupado.set(false);
        this.ref.close(undefined);
        this.exclusao.oferecerDesfazer(resultado.deletionBatchId);
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        if (this.semConexao(error)) {
          this.avisarSemConexao();
          return;
        }
        this.erro.set(problemMessage(error));
      },
    });
  }

  /**
   * Edição de compra (FR-015): PUT com todos os campos; valor negativo vira Crédito (R6). Em parcela,
   * `escopo` vale para descrição e observação (valor só esta; cartão e data todas — R7).
   */
  private salvarEdicaoCompra(
    original: CardPurchase,
    confirmarFaturaPaga: boolean,
    escopo: 'this' | 'all',
  ): void {
    const valor = this.valor();
    const descricao = this.description().trim();
    const notas = this.notes().trim();
    if (!this.compraEditadaValida() || !valor.ok) return;
    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      this.avisarSemConexao();
      return;
    }

    const corpo: CardPurchaseUpdate = {
      description: descricao,
      amount: centavosParaExpressao(Math.abs(valor.cents)),
      nature: valor.cents < 0 ? 'credit' : 'debit',
      date: this.date(),
      creditCardId: this.cardId(),
      notes: notas === '' ? null : notas,
      scope: escopo,
      version: original.version,
      tagIds: [...this.tagIds()],
    };
    if (confirmarFaturaPaga) corpo.confirmPaidInvoice = true;

    this.ocupado.set(true);
    this.compras.update(original.id, corpo).subscribe({
      next: () => {
        this.ocupado.set(false);
        this.avisos.open('Compra atualizada.', undefined, {
          duration: 4000,
          verticalPosition: 'top',
        });
        this.ref.close(undefined);
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        if (this.semConexao(error)) {
          this.avisarSemConexao();
          return;
        }
        const problem = asProblem(error);
        if (problem?.code === CODIGO_FATURA_PAGA) {
          this.pedirConfirmacaoFaturaPaga(problem.detail, () =>
            this.salvarEdicaoCompra(original, true, escopo),
          );
          return;
        }
        if (problem?.code === 'concurrency_conflict') {
          this.recarregarCompraPorConflito(original.id);
          return;
        }
        this.erro.set(problemMessage(error));
      },
    });
  }

  private recarregarCompraPorConflito(id: string): void {
    this.erro.set(MENSAGEM_CONFLITO);
    this.compras.get(id).subscribe({
      next: (compra) => {
        this.compraAtual = compra;
        this.preencherEdicaoCompra(compra);
        this.tentou.set(false);
      },
      error: (error: unknown) => {
        if (asProblem(error)?.code === 'not_found') {
          this.avisos.open('Esta compra não existe mais.', undefined, {
            duration: 4000,
            verticalPosition: 'top',
          });
          this.ref.close(undefined);
          return;
        }
        this.erro.set(problemMessage(error));
      },
    });
  }

  /** 409 (research R11): avisa e traz a versão atual do servidor para o formulário. */
  private recarregarPorConflito(id: string): void {
    this.erro.set(MENSAGEM_CONFLITO);
    this.entries.get(id).subscribe({
      next: (entry) => {
        this.atual = entry;
        this.preencherEdicao(entry);
        this.tentou.set(false);
      },
      error: (error: unknown) => {
        // Excluído em outro aparelho: não há mais o que editar.
        if (asProblem(error)?.code === 'not_found') {
          this.avisos.open('Este lançamento não existe mais.', undefined, {
            duration: 4000,
            verticalPosition: 'top',
          });
          this.ref.close(undefined);
          return;
        }
        this.erro.set(problemMessage(error));
      },
    });
  }

  protected abrirDuplicar(): void {
    this.duplicando.set(true);
    this.erroDuplicar.set(null);
    this.dataDuplicada.set(this.hoje());
    this.chaveDuplicar = novaChaveIdempotencia();
  }

  /** Compra à vista (não parcelada nem crédito) ou lançamento comum, ainda fora de recorrência. */
  protected get podeRepetir(): boolean {
    const compra = this.compraAtual;
    if (compra !== null) {
      return (
        !compra.recurrenceId &&
        compra.nature === 'debit' &&
        !compra.installmentPlanId &&
        (compra.installmentTotal ?? 1) <= 1
      );
    }
    return this.atual !== null && !this.atual.recurrenceId;
  }

  protected abrirRepetir(): void {
    const origem = this.compraAtual?.date ?? this.atual?.date ?? this.hoje();
    this.repetindo.set(true);
    this.erroRepetir.set(null);
    this.repetirFim.set('none');
    this.repetirMes.set(origem.slice(0, 7));
    this.repetirQtd.set('12');
    this.chaveRepetir = novaChaveIdempotencia();
  }

  protected cancelarRepetir(): void {
    this.repetindo.set(false);
  }

  /** FR-010: cria a recorrência a partir do lançamento salvo; ele vira a ocorrência do seu mês. */
  protected confirmarRepetir(): void {
    const compra = this.compraAtual;
    const origem = this.atual;
    if ((compra === null && origem === null) || this.ocupado()) return;
    const fim = this.repetirFim();
    const corpo: RecurrenceFromItem = {
      type: compra !== null ? 'card_purchase' : 'entry',
      id: compra?.id ?? origem!.id,
      endType: fim,
    };
    if (fim === 'until_month') {
      const mes = this.repetirMes();
      const inicio = (compra?.date ?? origem!.date).slice(0, 7);
      if (!isYearMonth(mes) || mes < inicio) {
        this.erroRepetir.set('Informe um mês de término a partir do mês do lançamento.');
        return;
      }
      corpo.endMonth = mes;
    } else if (fim === 'after_count') {
      const n = Number(this.repetirQtd());
      if (!Number.isInteger(n) || n < 1 || n > OCORRENCIAS_MAX) {
        this.erroRepetir.set(`Informe de 1 a ${OCORRENCIAS_MAX} ocorrências.`);
        return;
      }
      corpo.occurrenceLimit = n;
    }
    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      this.avisarSemConexao();
      return;
    }
    this.ocupado.set(true);
    this.erroRepetir.set(null);
    this.recorrencias.fromItem(corpo, this.chaveRepetir).subscribe({
      next: () => {
        this.ocupado.set(false);
        this.avisos.open('Agora este lançamento se repete todo mês.', undefined, {
          duration: 4000,
          verticalPosition: 'top',
        });
        if (compra !== null) this.compras.avisarAlteracao();
        else this.entries.avisarAlteracao(origem!);
        this.ref.close(origem ?? undefined);
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        if (this.semConexao(error)) {
          this.avisarSemConexao();
          return;
        }
        this.erroRepetir.set(problemMessage(error));
      },
    });
  }

  protected cancelarDuplicar(): void {
    this.duplicando.set(false);
  }

  /** FR-024: cria uma cópia com a nova data; mês e situação saem das regras de criação. */
  protected confirmarDuplicar(): void {
    const origem = this.atual;
    if (origem === null || this.ocupado()) return;
    const data = this.dataDuplicada();
    if (!DATA.test(data)) {
      this.erroDuplicar.set('Informe a nova data.');
      return;
    }
    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      this.avisarSemConexao();
      return;
    }
    this.ocupado.set(true);
    this.erroDuplicar.set(null);
    this.entries.duplicate(origem.id, data, this.chaveDuplicar).subscribe({
      next: (entry) => {
        this.ocupado.set(false);
        this.avisos.open('Lançamento duplicado.', undefined, {
          duration: 4000,
          verticalPosition: 'top',
        });
        this.ref.close(entry);
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        if (this.semConexao(error)) {
          this.avisarSemConexao();
          return;
        }
        this.erroDuplicar.set(problemMessage(error));
      },
    });
  }

  /** RT-09: exclui, fecha o formulário e oferece "Desfazer" por 8 s. */
  protected excluir(): void {
    const origem = this.atual;
    if (origem === null || this.ocupado()) return;
    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      this.avisarSemConexao();
      return;
    }
    this.ocupado.set(true);
    this.erro.set(null);
    this.exclusao.excluir('entry', origem.id).subscribe({
      next: (resultado) => {
        this.ocupado.set(false);
        this.ref.close(undefined);
        this.exclusao.oferecerDesfazer(resultado.deletionBatchId);
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        if (this.semConexao(error)) {
          this.avisarSemConexao();
          return;
        }
        this.erro.set(problemMessage(error));
      },
    });
  }

  protected fechar(): void {
    this.ref.close(undefined);
  }

  private montarCorpoTransferencia(): TransferCreate | null {
    const valor = this.valor();
    const descricao = this.description().trim();
    const notas = this.notes().trim();
    if (
      !valor.ok ||
      this.erroDescricao() !== null ||
      !DATA.test(this.date()) ||
      this.walletId() === '' ||
      this.erroDestino() !== null ||
      notas.length > OBSERVACAO_MAX
    ) {
      return null;
    }
    const corpo: TransferCreate = {
      fromWalletId: this.walletId(),
      toWalletId: this.toWalletId(),
      amount: this.amount().trim(),
      date: this.date(),
    };
    // Vazio → o servidor aplica "Transferência" (FR-021).
    if (descricao !== '') corpo.description = descricao;
    if (notas !== '') corpo.notes = notas;
    return corpo;
  }

  private montarCorpoCompra(confirmarFaturaPaga: boolean): CardPurchaseCreate | null {
    const descricao = this.description().trim();
    const notas = this.notes().trim();
    if (
      !this.valor().ok ||
      this.erroDescricao() !== null ||
      !DATA.test(this.date()) ||
      this.cartaoEscolhido() === null ||
      this.erroParcelas() !== null ||
      notas.length > OBSERVACAO_MAX
    ) {
      return null;
    }

    const corpo: CardPurchaseCreate = {
      description: descricao,
      amount: this.amount().trim(),
      date: this.date(),
      creditCardId: this.cardId(),
    };
    if (notas !== '') corpo.notes = notas;
    const tagsEnviadas = this.tagsEnviadas();
    if (tagsEnviadas.length > 0) corpo.tagIds = tagsEnviadas;
    if (this.emAndamento()) {
      corpo.installments = {
        total: this.parcelas(),
        mode: 'in_progress',
        nextNumber: this.proximaParcela(),
        firstInvoiceMonth: this.faturaEmAndamento(),
      };
    } else if (this.parcelas() > 1) {
      corpo.installments = { total: this.parcelas(), mode: this.modoParcela() };
    }
    if (confirmarFaturaPaga) corpo.confirmPaidInvoice = true;
    return corpo;
  }

  private carregarCartoes(): void {
    if (this.cartoesPedidos) return;
    this.cartoesPedidos = true;
    this.carregandoCartoes.set(true);
    // Na edição o cartão da compra pode estar arquivado: ele continua escolhível.
    this.cards
      .list(this.editandoCompra ? 'all' : 'false')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (lista) => {
          this.cartoes.set(
            this.compraAtual === null
              ? lista
              : lista.filter((c) => !c.archived || c.id === this.compraAtual!.creditCardId),
          );
          this.carregandoCartoes.set(false);
          this.resolverCartao();
        },
        error: (error: unknown) => {
          this.carregandoCartoes.set(false);
          this.cartoesPedidos = false;
          this.erroCartoes.set(problemMessage(error));
        },
      });
  }

  /** FR-010: último cartão usado se ainda ativo; senão o primeiro ativo em ordem alfabética. */
  private cartaoPadrao(): string {
    const ativos = this.cartoes();
    const ultimo = this.prefs.lastCardId();
    return (
      ativos.find((c) => c.id === ultimo)?.id ??
      [...ativos].sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'))[0]?.id ??
      ''
    );
  }

  private resolverCartao(): void {
    if (!this.cartoes().some((c) => c.id === this.cardId())) {
      this.cardManual.set(false);
      this.cardId.set(this.cartaoPadrao());
    }
  }

  private montarCorpo(): EntryCreate | null {
    if (this.kind() === 'transfer' || this.kind() === 'card') return null;
    const valor = this.valor();
    const descricao = this.description().trim();
    const notas = this.notes().trim();
    if (
      !valor.ok ||
      this.erroDescricao() !== null ||
      !DATA.test(this.date()) ||
      this.walletId() === '' ||
      notas.length > OBSERVACAO_MAX
    ) {
      return null;
    }

    const corpo: EntryCreate = {
      kind: this.kind() as EntryKind,
      description: descricao,
      amount: this.amount().trim(),
      date: this.date(),
      walletId: this.walletId(),
    };
    // Só enviamos o que a pessoa decidiu; o resto é regra do servidor (FR-012, RT-04).
    const status = this.statusManual();
    if (status !== null) corpo.status = status;
    const ref = this.refManual();
    if (ref !== null && ref !== '' && ref !== monthOfDate(this.date())) corpo.referenceMonth = ref;
    if (notas !== '') corpo.notes = notas;
    const tagsEnviadas = this.tagsEnviadas();
    if (tagsEnviadas.length > 0) corpo.tagIds = tagsEnviadas;
    return corpo;
  }

  /** Na divisão cada parte define a sua tag de acerto: `tagIds` leva só as tags comuns (R5). */
  private tagsEnviadas(): string[] {
    if (!this.dividindo()) return [...this.tagIds()];
    const acerto = new Set(
      this.catalogoTags()
        .filter((t) => t.isSettlement)
        .map((t) => t.id),
    );
    return this.tagIds().filter((id) => !acerto.has(id));
  }

  private aoSalvar(
    kind: LancamentoKind,
    walletId: string,
    lancarOutro: boolean,
    entry: Entry | undefined,
    mensagem = 'Lançamento salvo.',
  ): void {
    this.prefs.rememberSaved(kind, walletId);
    this.prefs.clearDraft();
    this.avisos.open(mensagem, undefined, { duration: 4000, verticalPosition: 'top' });

    if (!lancarOutro) {
      this.finalizado = true;
      this.ref.close(entry);
      return;
    }

    // FR-013: reabre limpo mantendo tipo, data e carteira.
    this.amount.set('');
    this.description.set('');
    this.notes.set('');
    this.tagIds.set([]);
    this.tagsManual.set(false);
    this.dividirAberto.set(false);
    this.statusManual.set(null);
    this.refManual.set(null);
    this.walletManual.set(false);
    this.toManual.set(false);
    this.cardManual.set(false);
    this.parcelas.set(1);
    this.emAndamento.set(false);
    this.proximaParcela.set(1);
    this.faturaManual.set(null);
    this.sugestoes.set([]);
    this.termo$.next('');
    this.tentou.set(false);
    this.maisOpcoes.set(false);
    this.chave = novaChaveIdempotencia();
    this.ultimoCorpo = '';
    this.valorCampo()?.focus();
  }

  private avisarSemConexao(): void {
    // No topo: embaixo o aviso cobriria os botões Salvar e impediria tentar de novo.
    this.avisos.open(MENSAGEM_SEM_CONEXAO_LANCAMENTO, 'Fechar', {
      duration: 8000,
      verticalPosition: 'top',
    });
  }

  private semConexao(error: unknown): boolean {
    return error instanceof HttpErrorResponse && error.status === 0;
  }

  /** FR-010: última carteira usada no tipo, se ainda ativa; senão a principal. */
  private carteiraPadrao(): string {
    const ativas = this.carteiras();
    const ultima = this.prefs.lastWalletId(this.kind());
    return (
      ativas.find((w) => w.id === ultima)?.id ??
      ativas.find((w) => w.isPrimary)?.id ??
      ativas[0]?.id ??
      ''
    );
  }

  private resolverCarteira(): void {
    const atual = this.walletId();
    if (!this.carteiras().some((w) => w.id === atual)) {
      this.walletManual.set(false);
      this.walletId.set(this.carteiraPadrao());
    }
    this.resolverDestino();
  }

  /**
   * "Para" da transferência: mantém a escolha à mão enquanto válida; senão a primeira carteira
   * diferente da origem (vazio quando só existe uma).
   */
  private resolverDestino(): void {
    const ativas = this.carteiras();
    const atual = this.toWalletId();
    const valido = ativas.some((w) => w.id === atual) && atual !== this.walletId();
    if (valido) return;
    this.toManual.set(false);
    this.toWalletId.set(ativas.find((w) => w.id !== this.walletId())?.id ?? '');
  }

  /** US3.1: Entrada Prevista com a tag, o mês de referência e a data da cobrança; sem rascunho. */
  private aplicarPreenchimento(p: EntradaPreenchida): void {
    this.kind.set('income');
    this.amount.set(centavosParaExpressao(p.amountCents));
    this.description.set(p.description);
    this.date.set(p.date);
    this.statusManual.set('planned');
    this.refManual.set(p.referenceMonth);
    this.tagIds.set([...p.tagIds]);
    this.tagsManual.set(true);
    this.maisOpcoes.set(p.referenceMonth !== monthOfDate(p.date));
  }

  /** A carteira da cobrança é a principal, e não a última usada em Entradas (US3.1). */
  private usarCarteiraPrincipalDaCobranca(): void {
    if (this.preenchida === null) return;
    const principal = this.carteiras().find((w) => w.isPrimary);
    if (principal === undefined) return;
    this.walletId.set(principal.id);
    this.walletManual.set(true);
  }

  private preencherEdicaoCompra(compra: CardPurchase): void {
    this.kind.set('card');
    this.amount.set(
      centavosParaExpressao(compra.nature === 'credit' ? -compra.amountCents : compra.amountCents),
    );
    this.description.set(compra.description);
    this.date.set(compra.date);
    this.cardId.set(compra.creditCardId);
    this.cardManual.set(true);
    this.tagIds.set([...(compra.tagIds ?? [])]);
    this.notes.set(compra.notes ?? '');
    if (this.notes() !== '') this.maisOpcoes.set(true);
  }

  private preencherEdicao(entry: Entry): void {
    this.kind.set(entry.kind);
    this.refManual.set(null);
    this.amount.set(centavosParaExpressao(entry.amountCents));
    this.description.set(entry.description);
    this.date.set(entry.date);
    this.walletId.set(entry.walletId);
    this.walletManual.set(true);
    this.tagIds.set([...(entry.tagIds ?? [])]);
    this.statusManual.set(entry.status);
    if (entry.referenceMonth !== monthOfDate(entry.date)) {
      this.refManual.set(entry.referenceMonth);
      this.maisOpcoes.set(true);
    }
    this.notes.set(entry.notes ?? '');
    if (this.notes() !== '') this.maisOpcoes.set(true);
  }

  private restaurarRascunho(): void {
    const r = this.prefs.readDraft<Rascunho>();
    if (r === null || typeof r !== 'object') return;
    if ((r.amount ?? '') === '' && (r.description ?? '') === '' && (r.notes ?? '') === '') return;

    if (r.kind === 'card' || r.kind === 'expense' || r.kind === 'income' || r.kind === 'transfer') {
      this.kind.set(r.kind);
    }
    this.amount.set(r.amount ?? '');
    this.description.set(r.description ?? '');
    this.notes.set(r.notes ?? '');
    if (r.savedOn === this.hoje() && DATA.test(r.date)) this.date.set(r.date);
    if (r.status === 'planned' || r.status === 'settled') this.statusManual.set(r.status);
    if (typeof r.refMonth === 'string') this.refManual.set(r.refMonth);
    if (r.walletId) {
      this.walletId.set(r.walletId);
      this.walletManual.set(r.walletManual === true);
    }
    if (r.cardId) {
      this.cardId.set(r.cardId);
      this.cardManual.set(r.cardManual === true);
    }
    if (r.toWalletId) {
      this.toWalletId.set(r.toWalletId);
      this.toManual.set(r.toManual === true);
    }
    if (Array.isArray(r.tagIds)) {
      this.tagIds.set(r.tagIds.filter((id): id is string => typeof id === 'string'));
      this.tagsManual.set(r.tagsManual === true);
    }
    this.maisOpcoes.set(r.refMonth != null || (r.notes ?? '') !== '');
  }

  private guardarRascunho(): void {
    // Edição e cobrança pré-preenchida não mexem no rascunho da pessoa.
    if (this.editando || this.preenchida !== null) return;
    const rascunho: Rascunho = {
      savedOn: this.hoje(),
      kind: this.kind(),
      amount: this.amount(),
      description: this.description(),
      date: this.date(),
      walletId: this.walletId(),
      walletManual: this.walletManual(),
      toWalletId: this.toWalletId(),
      toManual: this.toManual(),
      cardId: this.cardId(),
      cardManual: this.cardManual(),
      status: this.statusManual(),
      refMonth: this.refManual(),
      notes: this.notes(),
      tagIds: this.tagIds(),
      tagsManual: this.tagsManual(),
    };
    if (
      this.finalizado ||
      (rascunho.amount === '' && rascunho.description === '' && rascunho.notes === '')
    ) {
      if (!this.finalizado) this.prefs.clearDraft();
      return;
    }
    this.prefs.saveDraft(rascunho);
  }
}
