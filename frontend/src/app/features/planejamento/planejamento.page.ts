import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { ActivatedRoute, Router } from '@angular/router';
import { EMPTY, Subject, catchError, concatMap, of, switchMap, tap } from 'rxjs';
import { asProblem, problemMessage } from '../../core/errors/problem-details';
import {
  BrlPipe,
  monthShortLabel,
  parseValueExpression,
  today,
  valueExpressionMessages,
} from '../../core/money';
import { centavosParaCampo } from '../carteiras/carteira-form.page';
import { Wallet } from '../carteiras/wallet.models';
import { WalletsService } from '../carteiras/wallets.service';
import { EntriesService } from '../lancamento/entries.service';
import { ExclusaoService } from '../lancamento/exclusao.service';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { ANO_MAXIMO, ANO_MINIMO, lerAno } from '../painel/painel-logic';
import { CopiarData, CopiarDialog, CopiarResultado } from './copiar.dialog';
import { CelulaData, CelulaDialog, CelulaResultado } from './celula.dialog';
import { ItensCelulaData, ItensCelulaDialog, ItensCelulaResultado } from './itens-celula.dialog';
import { NovaLinhaData, NovaLinhaDialog, NovaLinhaResultado } from './nova-linha.dialog';
import {
  CelulaRef,
  aplicarFatura,
  aplicarLinha,
  linhaVazia,
  mesclar,
  mesesDoAno,
  primeiroMesComValor,
  valorMaisFrequente,
  vizinha,
  chaveDaLinha,
} from './planejamento-logic';
import {
  CELULA_NAO_EDITAVEL,
  InvoiceCell,
  InvoiceRow,
  PlanningCell,
  PlanningCellInput,
  PlanningCellItem,
  PlanningFillInput,
  PlanningGrid,
  PlanningRow,
  PlanningTotals,
} from './planning.models';
import { PlanningService } from './planning.service';
import { PreencherData, PreencherDialog, PreencherResultado } from './preencher.dialog';

/** Largura a partir da qual a grade é editada inline (abaixo disso, por diálogo). */
const DESKTOP = '(min-width: 1024px)';

/** Linha da tabela (de Entrada/Saída ou de fatura) já com um id estável para navegar entre células. */
interface LinhaTabela {
  id: string;
  tipo: 'income' | 'expense' | 'invoice';
  rotulo: string;
  arquivada: boolean;
  nova: boolean;
  cells: (PlanningCell | InvoiceCell)[];
  entrada?: PlanningRow;
  fatura?: InvoiceRow;
}

interface Edicao {
  linha: string;
  mes: string;
  texto: string;
}

/**
 * Tela `/planejamento/:ano` (007 US1, FR-001 a FR-008; research R5): grade do ano com edição inline
 * (Enter/Tab salvam e andam, Esc cancela), totais que se atualizam a cada edição, "Nova linha",
 * "Preencher meses", "Transformar em recorrência" e a lista de lançamentos das células não editáveis.
 * No celular, a 1ª coluna fica fixa, os meses rolam e a célula é editada por diálogo. Não calcula
 * valores: exibe o que a API devolve.
 */
@Component({
  selector: 'app-planejamento-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrlPipe, MatIconModule, MatMenuModule],
  templateUrl: './planejamento.page.html',
  styleUrl: './planejamento.page.scss',
})
export class PlanejamentoPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(PlanningService);
  private readonly wallets = inject(WalletsService);
  private readonly entries = inject(EntriesService);
  private readonly exclusao = inject(ExclusaoService);
  private readonly launcher = inject(LancamentoLauncher);
  private readonly dialog = inject(MatDialog);

  private readonly campo = viewChild<ElementRef<HTMLInputElement>>('campoCelula');
  private readonly fila = new Subject<{ linha: LinhaTabela; mes: string; texto: string }>();
  private readonly consulta = typeof matchMedia === 'function' ? matchMedia(DESKTOP) : null;

  protected readonly desktop = signal(this.consulta?.matches ?? true);
  protected readonly ano = signal(0);
  protected readonly grade = signal<PlanningGrid | null>(null);
  protected readonly totais = signal<PlanningTotals | null>(null);
  protected readonly novas = signal<PlanningRow[]>([]);
  protected readonly carteiras = signal<Wallet[]>([]);
  protected readonly editando = signal<Edicao | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly aviso = signal<string | null>(null);
  protected readonly ocupado = signal(0);

  protected readonly noMinimo = computed(() => this.ano() <= ANO_MINIMO);
  protected readonly noMaximo = computed(() => this.ano() >= ANO_MAXIMO);
  protected readonly meses = computed(() => mesesDoAno(this.ano()));
  protected readonly rotulosMeses = computed(() =>
    this.meses().map((m) => ({ mes: m, rotulo: monthShortLabel(m).slice(0, 3) })),
  );
  protected readonly mesDeHoje = computed(() => {
    const hoje = this.grade()?.today ?? today();
    return hoje.startsWith(`${this.ano()}-`) ? hoje.slice(0, 7) : null;
  });

  protected readonly entradas = computed(() =>
    this.tabela('income', mesclar(this.grade()?.incomeRows ?? [], this.novasDe('income'))),
  );
  protected readonly saidas = computed(() =>
    this.tabela('expense', mesclar(this.grade()?.expenseRows ?? [], this.novasDe('expense'))),
  );
  protected readonly faturas = computed<LinhaTabela[]>(() =>
    (this.grade()?.invoiceRows ?? []).map((f) => ({
      id: `invoice:${f.creditCardId}`,
      tipo: 'invoice',
      rotulo: `Fatura ${f.creditCardName}`,
      arquivada: f.archived,
      nova: false,
      cells: f.cells,
      fatura: f,
    })),
  );
  private readonly todasAsLinhas = computed(() => [
    ...this.entradas(),
    ...this.saidas(),
    ...this.faturas(),
  ]);
  private readonly idsDasLinhas = computed(() => this.todasAsLinhas().map((l) => l.id));

  constructor() {
    this.consulta?.addEventListener?.('change', (e) => this.desktop.set(e.matches));

    this.wallets
      .list()
      .pipe(takeUntilDestroyed())
      .subscribe({ next: (lista) => this.carteiras.set(lista), error: () => undefined });

    this.route.paramMap
      .pipe(
        switchMap((params) => {
          const ano = lerAno(params.get('ano'));
          if (ano === null) {
            void this.router.navigate(['/planejamento', today().slice(0, 4)], {
              replaceUrl: true,
            });
            return of(null);
          }
          this.ano.set(ano);
          this.grade.set(null);
          this.totais.set(null);
          this.novas.set([]);
          this.editando.set(null);
          this.erro.set(null);
          this.aviso.set(null);
          return this.service.grid(ano).pipe(
            catchError((error: unknown) => {
              this.erro.set(problemMessage(error));
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((grade) => {
        if (grade !== null) this.receber(grade);
      });

    // Edições em fila: uma por vez, para os totais nunca chegarem fora de ordem (FR-008).
    this.fila
      .pipe(
        concatMap((e) => this.salvar(e.linha, e.mes, e.texto)),
        takeUntilDestroyed(),
      )
      .subscribe();

    // Desfazer uma exclusão (RT-09) muda a grade por fora.
    this.exclusao.mudou$.pipe(takeUntilDestroyed()).subscribe(() => this.recarregar());

    // Foco no campo da célula que acabou de abrir.
    effect(() => {
      const el = this.campo()?.nativeElement;
      if (el) {
        el.focus();
        el.select();
      }
    });
  }

  protected ir(delta: -1 | 1): void {
    void this.router.navigate(['/planejamento', this.ano() + delta]);
  }

  // ---- Células ----

  protected valorDaCelula(c: PlanningCell | InvoiceCell): number | null {
    if ('itemCount' in c) return c.itemCount > 0 ? c.plannedCents : null;
    return c.plannedCents > 0 || c.estimateCents ? c.plannedCents : null;
  }

  protected temEstimativa(c: PlanningCell | InvoiceCell): c is InvoiceCell {
    return 'estimateCents' in c && c.estimateCents !== null && c.estimateCents !== undefined;
  }

  protected todosEfetivados(c: PlanningCell | InvoiceCell): boolean {
    return 'allSettled' in c && c.itemCount > 0 && c.allSettled;
  }

  protected naoEditavel(c: PlanningCell | InvoiceCell): boolean {
    return !c.editable;
  }

  protected emEdicao(linha: LinhaTabela, mes: string): boolean {
    const e = this.editando();
    return e !== null && e.linha === linha.id && e.mes === mes;
  }

  protected rotuloDaCelula(linha: LinhaTabela, mes: string): string {
    return `${linha.rotulo}, ${monthShortLabel(mes).slice(0, 3)}`;
  }

  /** Texto inicial do campo: o valor da célula (da estimativa, na fatura) ou vazio. */
  private textoInicial(c: PlanningCell | InvoiceCell): string {
    if ('itemCount' in c) return c.itemCount > 0 ? centavosParaCampo(c.plannedCents) : '';
    return c.estimateCents ? centavosParaCampo(c.estimateCents) : '';
  }

  protected clicarCelula(linha: LinhaTabela, cell: PlanningCell | InvoiceCell): void {
    if (!cell.editable) {
      this.abrirItens(linha, cell as PlanningCell);
      return;
    }
    if (this.desktop()) {
      this.editando.set({ linha: linha.id, mes: cell.month, texto: this.textoInicial(cell) });
      return;
    }
    this.editarPorDialogo(linha, cell);
  }

  private editarPorDialogo(linha: LinhaTabela, cell: PlanningCell | InvoiceCell): void {
    const data: CelulaData = {
      titulo: this.rotuloDaCelula(linha, cell.month),
      valorInicial: this.textoInicial(cell),
    };
    this.dialog
      .open<CelulaDialog, CelulaData, CelulaResultado>(CelulaDialog, { data })
      .afterClosed()
      .subscribe((texto) => {
        if (texto !== null && texto !== undefined) this.enfileirar(linha, cell.month, texto);
      });
  }

  protected digitar(evento: Event): void {
    const e = this.editando();
    if (e !== null) this.editando.set({ ...e, texto: (evento.target as HTMLInputElement).value });
  }

  protected teclar(evento: KeyboardEvent, linha: LinhaTabela, mes: string): void {
    if (evento.key === 'Escape') {
      evento.preventDefault();
      this.editando.set(null);
      return;
    }
    let direcao: 'baixo' | 'direita' | 'esquerda' | null = null;
    if (evento.key === 'Enter') direcao = 'baixo';
    else if (evento.key === 'Tab') direcao = evento.shiftKey ? 'esquerda' : 'direita';
    if (direcao === null) return;
    evento.preventDefault();
    this.confirmar(linha, mes, direcao);
  }

  protected sairDoCampo(linha: LinhaTabela, mes: string): void {
    this.confirmar(linha, mes, null);
  }

  /** Salva o texto da célula em edição e, se pedido, abre a vizinha. */
  private confirmar(
    linha: LinhaTabela,
    mes: string,
    direcao: 'baixo' | 'direita' | 'esquerda' | null,
  ): void {
    const atual = this.editando();
    if (atual === null || atual.linha !== linha.id || atual.mes !== mes) return;

    const destino =
      direcao === null
        ? null
        : vizinha(this.idsDasLinhas(), this.meses(), { linha: linha.id, mes }, direcao);
    const alvo = destino === null ? null : this.celulaDe(destino);
    this.editando.set(
      destino !== null && alvo !== null && alvo.cell.editable
        ? { linha: destino.linha, mes: destino.mes, texto: this.textoInicial(alvo.cell) }
        : null,
    );

    const original = this.celulaDe({ linha: linha.id, mes })?.cell;
    if (original !== undefined && atual.texto.trim() === this.textoInicial(original).trim()) return;
    this.enfileirar(linha, mes, atual.texto);
  }

  private celulaDe(
    ref: CelulaRef,
  ): { linha: LinhaTabela; cell: PlanningCell | InvoiceCell } | null {
    const linha = this.todasAsLinhas().find((l) => l.id === ref.linha);
    const cell = linha?.cells.find((c) => c.month === ref.mes);
    return linha && cell ? { linha, cell } : null;
  }

  private enfileirar(linha: LinhaTabela, mes: string, texto: string): void {
    this.erro.set(null);
    this.aviso.set(null);
    const cell = linha.cells.find((c) => c.month === mes);
    const vazio = texto.trim() === '';
    if (vazio && cell !== undefined && this.textoInicial(cell) === '') return;
    if (!vazio) {
      const analise = parseValueExpression(texto, false);
      if (!analise.ok) {
        this.erro.set(
          `${this.rotuloDaCelula(linha, mes)}: ${valueExpressionMessages[analise.error]}`,
        );
        return;
      }
    }
    this.fila.next({ linha, mes, texto });
  }

  private entradaDaCelula(linha: LinhaTabela, mes: string, texto: string): PlanningCellInput {
    if (linha.fatura) {
      return {
        kind: 'invoice',
        creditCardId: linha.fatura.creditCardId,
        month: mes,
        value: texto.trim(),
      };
    }
    const row = linha.entrada!;
    const input: PlanningCellInput = {
      kind: row.kind,
      description: row.description,
      month: mes,
      value: texto.trim(),
    };
    if (linha.nova) {
      input.walletId = row.latestWalletId;
      input.day = row.latestDay;
    }
    return input;
  }

  private salvar(linha: LinhaTabela, mes: string, texto: string) {
    this.ocupado.update((n) => n + 1);
    return this.service.setCell(this.ano(), this.entradaDaCelula(linha, mes, texto)).pipe(
      tap((resultado) => {
        this.aplicar(resultado.row, resultado.totals);
        if (resultado.deletionBatchId) this.exclusao.oferecerDesfazer(resultado.deletionBatchId);
      }),
      catchError((error: unknown) => {
        this.tratarErroDeCelula(error, linha, mes);
        return EMPTY;
      }),
      tap({ finalize: () => this.ocupado.update((n) => n - 1) }),
    );
  }

  private tratarErroDeCelula(error: unknown, linha: LinhaTabela, mes: string): void {
    const problema = asProblem(error);
    if (problema?.code === CELULA_NAO_EDITAVEL) {
      const items = (problema as { items?: PlanningCellItem[] }).items ?? [];
      this.mostrarItens(this.rotuloDaCelula(linha, mes), items);
      return;
    }
    this.erro.set(`${this.rotuloDaCelula(linha, mes)}: ${problemMessage(error)}`);
  }

  // ---- Lista de lançamentos da célula (US1.5) ----

  /**
   * Célula não editável: a API devolve os lançamentos no 409 de uma edição. O valor reenviado é o
   * que já está na célula, então nada muda (a célula continua não editável).
   */
  private abrirItens(linha: LinhaTabela, cell: PlanningCell): void {
    if (linha.fatura) return;
    this.service
      .setCell(this.ano(), {
        ...this.entradaDaCelula(linha, cell.month, centavosParaCampo(cell.plannedCents)),
      })
      .subscribe({
        next: (resultado) => this.aplicar(resultado.row, resultado.totals),
        error: (error: unknown) => this.tratarErroDeCelula(error, linha, cell.month),
      });
  }

  private mostrarItens(titulo: string, items: PlanningCellItem[]): void {
    this.dialog
      .open<ItensCelulaDialog, ItensCelulaData, ItensCelulaResultado>(ItensCelulaDialog, {
        data: { titulo, items },
      })
      .afterClosed()
      .subscribe((id) => {
        if (id) this.editarLancamento(id);
      });
  }

  private editarLancamento(id: string): void {
    this.entries.get(id).subscribe({
      next: (entry) =>
        this.launcher
          .abrir(entry)
          .afterClosed()
          .subscribe(() => this.recarregar()),
      error: (error: unknown) => this.erro.set(problemMessage(error)),
    });
  }

  // ---- Menus da linha ----

  protected novaLinha(): void {
    const ativas = this.carteiras().filter((w) => !w.archived);
    const data: NovaLinhaData = {
      carteiras: ativas.map((w) => ({ id: w.id, name: w.name })),
      carteiraPadrao: (ativas.find((w) => w.isPrimary) ?? ativas[0])?.id ?? '',
    };
    this.dialog
      .open<NovaLinhaDialog, NovaLinhaData, NovaLinhaResultado | null>(NovaLinhaDialog, { data })
      .afterClosed()
      .subscribe((r) => {
        if (r) this.criarLinha(r);
      });
  }

  private criarLinha(r: NovaLinhaResultado): void {
    const chave = chaveDaLinha(r.kind, r.description);
    const existe = [...(this.grade()?.incomeRows ?? []), ...(this.grade()?.expenseRows ?? [])]
      .concat(this.novas())
      .some((l) => l.key === chave);
    if (existe) {
      this.erro.set(`Já existe uma linha "${r.description}" neste tipo.`);
      return;
    }
    this.erro.set(null);
    this.novas.update((l) => [
      ...l,
      linhaVazia(this.ano(), r.kind, r.description, r.walletId, r.day),
    ]);
  }

  protected preencher(linha: LinhaTabela): void {
    const data: PreencherData = {
      titulo: `Preencher meses — ${linha.rotulo}`,
      meses: this.meses(),
      mesesComValor: linha.cells
        .filter((c) => ('itemCount' in c ? c.itemCount > 0 : !!c.estimateCents))
        .map((c) => c.month),
    };
    const chave = crypto.randomUUID();
    this.dialog
      .open<PreencherDialog, PreencherData, PreencherResultado | null>(PreencherDialog, { data })
      .afterClosed()
      .subscribe((r) => {
        if (r) this.executarPreenchimento(linha, r, chave);
      });
  }

  private executarPreenchimento(linha: LinhaTabela, r: PreencherResultado, chave: string): void {
    const input: PlanningFillInput = linha.fatura
      ? {
          kind: 'invoice',
          creditCardId: linha.fatura.creditCardId,
          amount: r.amount,
          months: r.months,
        }
      : {
          kind: linha.entrada!.kind,
          description: linha.entrada!.description,
          amount: r.amount,
          months: r.months,
          ...(linha.nova
            ? { walletId: linha.entrada!.latestWalletId, day: linha.entrada!.latestDay }
            : {}),
        };
    this.erro.set(null);
    this.aviso.set(null);
    this.ocupado.update((n) => n + 1);
    this.service
      .fill(this.ano(), input, chave)
      .pipe(tap({ finalize: () => this.ocupado.update((n) => n - 1) }))
      .subscribe({
        next: (resultado) => {
          this.aplicar(resultado.row, resultado.totals);
          this.aviso.set(textoDoPreenchimento(resultado.created, resultado.skipped));
        },
        error: (error: unknown) => this.erro.set(`${linha.rotulo}: ${problemMessage(error)}`),
      });
  }

  /** US3: diálogo "Copiar do ano anterior"; nada é criado sem confirmação (FR-013). */
  protected copiarDoAnoAnterior(): void {
    this.dialog
      .open<CopiarDialog, CopiarData, CopiarResultado>(CopiarDialog, {
        data: { ano: this.ano() },
      })
      .afterClosed()
      .subscribe((r) => {
        if (!r) return;
        this.aviso.set(textoDaCopia(r.created, r.skipped));
        this.recarregar();
      });
  }

  /** US1.9: abre o cadastro de recorrência (005) com os dados da linha. */
  protected transformarEmRecorrencia(linha: LinhaTabela): void {
    const row = linha.entrada;
    if (!row) return;
    const valor = valorMaisFrequente(row.cells);
    const queryParams: Record<string, string> = {
      kind: row.kind,
      description: row.description,
      dayOfMonth: String(row.latestDay ?? 1),
      startMonth: primeiroMesComValor(row.cells) ?? this.meses()[0],
    };
    if (valor > 0) queryParams['amount'] = centavosParaCampo(valor);
    if (row.latestWalletId) queryParams['walletId'] = row.latestWalletId;
    void this.router.navigate(['/recorrencias/nova'], { queryParams });
  }

  // ---- Estado ----

  private recarregar(): void {
    const ano = this.ano();
    this.service.grid(ano).subscribe({
      next: (grade) => {
        if (grade.year === this.ano()) this.receber(grade);
      },
      error: (error: unknown) => this.erro.set(problemMessage(error)),
    });
  }

  private receber(grade: PlanningGrid): void {
    this.grade.set(grade);
    this.totais.set(grade.totals);
  }

  /** Aplica a linha devolvida pela API e os totais dos 12 meses (FR-008). */
  private aplicar(row: PlanningRow | InvoiceRow, totals: PlanningTotals): void {
    this.grade.update((g) => {
      if (g === null) return g;
      if ('creditCardId' in row) return { ...g, invoiceRows: aplicarFatura(g.invoiceRows, row) };
      return row.kind === 'income'
        ? { ...g, incomeRows: aplicarLinha(g.incomeRows, row) }
        : { ...g, expenseRows: aplicarLinha(g.expenseRows, row) };
    });
    this.totais.set(totals);
  }

  private novasDe(tipo: 'income' | 'expense'): PlanningRow[] {
    return this.novas().filter((n) => n.kind === tipo);
  }

  private tabela(tipo: 'income' | 'expense', linhas: PlanningRow[]): LinhaTabela[] {
    const novas = new Set(this.novas().map((n) => n.key));
    const doServidor = new Set(
      (tipo === 'income' ? this.grade()?.incomeRows : this.grade()?.expenseRows)?.map((l) => l.key),
    );
    return linhas.map((l) => ({
      id: l.key,
      tipo,
      rotulo: l.description,
      arquivada: false,
      nova: novas.has(l.key) && !doServidor.has(l.key),
      cells: l.cells,
      entrada: l,
    }));
  }
}

function textoDoPreenchimento(criados: number, pulados: number): string {
  const criou = criados === 1 ? '1 mês preenchido' : `${criados} meses preenchidos`;
  if (pulados === 0) return `${criou}.`;
  return `${criou}; ${pulados === 1 ? '1 mês já tinha' : `${pulados} meses já tinham`} valor e foi pulado.`;
}

function textoDaCopia(criados: number, pulados: number): string {
  const criou = criados === 1 ? '1 item copiado' : `${criados} itens copiados`;
  if (pulados === 0) return `${criou}.`;
  return `${criou}; ${pulados === 1 ? '1 já tinha' : `${pulados} já tinham`} valor e foi pulado.`;
}
