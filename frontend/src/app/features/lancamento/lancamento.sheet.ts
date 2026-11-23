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
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, catchError, of, switchMap, timer } from 'rxjs';
import { asProblem, problemMessage } from '../../core/errors/problem-details';
import { formatBrl, monthOfDate, parseValueExpression, today } from '../../core/money';
import { DevicePrefsService, LancamentoKind } from '../../core/prefs/device-prefs.service';
import { AmountInput } from '../../shared/amount-input/amount-input';
import { WALLET_COLORS, Wallet } from '../carteiras/wallet.models';
import { WalletsService } from '../carteiras/wallets.service';
import { EntriesService } from './entries.service';
import { TransfersService } from './transfers.service';
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
import { novaChaveIdempotencia, situacaoPadrao } from './lancamento-logic';

export const MENSAGEM_SEM_CONEXAO_LANCAMENTO =
  'Sem conexão. O lançamento não foi salvo; tente de novo.';
export const ERRO_MESMA_CARTEIRA = 'Escolha carteiras diferentes.';
export const AVISO_DATA_ANTERIOR =
  'Esta data é anterior ao saldo inicial da carteira. O saldo inicial já deveria incluir este valor.';

const DATA = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;

/** Dados do diálogo: com `entry`, o formulário abre em modo edição (T012; o completo é a T018). */
export interface LancamentoSheetData {
  entry?: Entry;
}

/** Centavos como expressão de valor aceita pelo servidor: `4499` → `44,99`. */
function centavosParaExpressao(cents: number): string {
  const reais = Math.floor(cents / 100);
  return `${reais},${(cents % 100).toString().padStart(2, '0')}`;
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
  status: EntryStatus | null;
  refMonth: string | null;
  notes: string;
}

/**
 * Formulário rápido de lançamento (US2; FR-008..FR-013; contracts/ui-rotas.md). Aberto pelo "+"
 * da casca (`LancamentoLauncher`): tela cheia no celular, diálogo no computador. Cria Saída,
 * Entrada e Transferência (De/Para, sem situação nem sugestões; US5). Com `entry` nos dados do diálogo abre para editar
 * (salva por PUT); Duplicar, Excluir e o tratamento de conflito de versão são da T018.
 */
@Component({
  selector: 'app-lancamento-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatIconModule, AmountInput],
  templateUrl: './lancamento.sheet.html',
  styleUrl: './lancamento.sheet.scss',
})
export class LancamentoSheet {
  private readonly ref = inject<MatDialogRef<LancamentoSheet, Entry | undefined>>(MatDialogRef);
  private readonly entries = inject(EntriesService);
  private readonly transfers = inject(TransfersService);
  private readonly wallets = inject(WalletsService);
  private readonly prefs = inject(DevicePrefsService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  private readonly edicao =
    inject<LancamentoSheetData | null>(MAT_DIALOG_DATA, { optional: true })?.entry ?? null;
  private readonly valorCampo = viewChild(AmountInput);

  protected readonly editando = this.edicao !== null;
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
  protected readonly maisOpcoes = signal(false);

  protected readonly carteiras = signal<Wallet[]>([]);
  protected readonly carregandoCarteiras = signal(true);
  protected readonly sugestoes = signal<EntrySuggestion[]>([]);
  protected readonly tentou = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);

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
  protected readonly descricaoPadrao = DESCRICAO_TRANSFERENCIA;
  protected readonly rotuloRealizada = computed(() =>
    this.kind() === 'income' ? 'Recebida' : 'Paga',
  );

  protected readonly valor = computed(() => parseValueExpression(this.amount()));
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

  protected readonly termo$ = new Subject<string>();
  private chave = novaChaveIdempotencia();
  private ultimoCorpo = '';
  private finalizado = false;

  constructor() {
    if (this.edicao !== null) this.preencherEdicao(this.edicao);
    else this.restaurarRascunho();

    this.termo$
      .pipe(
        // `timer` (e não `debounceTime`): não depende de `Date.now`, que os testes congelam.
        switchMap((q) => {
          const kind = this.kind();
          // Transferência não tem sugestões (contracts/ui-rotas.md).
          if (kind === 'transfer' || q.trim().length < 1) return of<EntrySuggestion[]>([]);
          return timer(120).pipe(
            switchMap(() => this.entries.suggestions(q.trim(), kind)),
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
        },
        error: (error: unknown) => {
          this.carregandoCarteiras.set(false);
          this.erro.set(problemMessage(error));
        },
      });

    effect(() => this.guardarRascunho());

    afterNextRender(() => this.valorCampo()?.focus());
  }

  protected escolherTipo(kind: LancamentoKind): void {
    if (this.kind() === kind) return;
    this.kind.set(kind);
    this.sugestoes.set([]);
    this.termo$.next(this.description());
    if (!this.walletManual()) this.walletId.set(this.carteiraPadrao());
    this.resolverDestino();
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
    const ativa = s.walletId && this.carteiras().some((w) => w.id === s.walletId);
    if (!this.walletManual() && ativa) this.walletId.set(s.walletId!);
  }

  protected escolherCarteira(id: string): void {
    this.walletId.set(id);
    this.walletManual.set(true);
    this.resolverDestino();
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

    if (this.edicao !== null) {
      this.salvarEdicao(this.edicao);
      return;
    }

    if (this.ehTransferencia()) {
      this.salvarTransferencia(lancarOutro);
      return;
    }

    const corpo = this.montarCorpo();
    if (corpo === null) return;

    // Mesma chave só para reenvio idêntico: outra chave com o mesmo corpo seria um lançamento novo,
    // e a mesma chave com corpo diferente é recusada (422 `idempotency.key_reused`).
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
    this.entries.create(corpo, this.chave).subscribe({
      next: (entry) => {
        this.ocupado.set(false);
        this.aoSalvar(entry.kind, entry.walletId, lancarOutro, entry);
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
    };
    this.ocupado.set(true);
    this.entries.update(original.id, corpo).subscribe({
      next: (entry) => {
        this.ocupado.set(false);
        this.snackBar.open('Lançamento atualizado.', undefined, {
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

  private montarCorpo(): EntryCreate | null {
    if (this.kind() === 'transfer') return null;
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
    return corpo;
  }

  private aoSalvar(
    kind: LancamentoKind,
    walletId: string,
    lancarOutro: boolean,
    entry: Entry | undefined,
  ): void {
    this.prefs.rememberSaved(kind, walletId);
    this.prefs.clearDraft();
    this.snackBar.open('Lançamento salvo.', undefined, { duration: 4000, verticalPosition: 'top' });

    if (!lancarOutro) {
      this.finalizado = true;
      this.ref.close(entry);
      return;
    }

    // FR-013: reabre limpo mantendo tipo, data e carteira.
    this.amount.set('');
    this.description.set('');
    this.notes.set('');
    this.statusManual.set(null);
    this.refManual.set(null);
    this.walletManual.set(false);
    this.toManual.set(false);
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
    this.snackBar.open(MENSAGEM_SEM_CONEXAO_LANCAMENTO, 'Fechar', {
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

  private preencherEdicao(entry: Entry): void {
    this.kind.set(entry.kind);
    this.amount.set(centavosParaExpressao(entry.amountCents));
    this.description.set(entry.description);
    this.date.set(entry.date);
    this.walletId.set(entry.walletId);
    this.walletManual.set(true);
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

    if (r.kind === 'expense' || r.kind === 'income' || r.kind === 'transfer') this.kind.set(r.kind);
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
    if (r.toWalletId) {
      this.toWalletId.set(r.toWalletId);
      this.toManual.set(r.toManual === true);
    }
    this.maisOpcoes.set(r.refMonth != null || (r.notes ?? '') !== '');
  }

  private guardarRascunho(): void {
    if (this.editando) return;
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
      status: this.statusManual(),
      refMonth: this.refManual(),
      notes: this.notes(),
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
