import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { asProblem, problemMessage } from '../../core/errors/problem-details';
import {
  isYearMonth,
  parseValueExpression,
  today,
  valueExpressionMessages,
} from '../../core/money';
import { AmountInput } from '../../shared/amount-input/amount-input';
import { TagPicker } from '../../shared/tag-picker/tag-picker';
import { CardsService } from '../cartoes/cards.service';
import { ConfirmarDialog, ConfirmarData, ConfirmarResultado } from '../carteiras/confirmar.dialog';
import { centavosParaCampo } from '../carteiras/carteira-form.page';
import { WalletsService } from '../carteiras/wallets.service';
import { DESCRICAO_MAX } from '../lancamento/entry.models';
import { APartirDeData, APartirDeDialog, APartirDeResultado } from './a-partir-de.dialog';
import {
  OCORRENCIAS_MAX,
  RECURRENCE_END_TYPES,
  RECURRENCE_KINDS,
  REFERENCE_MONTH_RULES,
  Recurrence,
  RecurrenceEndType,
  RecurrenceInput,
  RecurrenceKind,
  ReferenceMonthRule,
} from './recurrence.models';
import { RecurrencesService } from './recurrences.service';

interface Opcao {
  id: string;
  name: string;
}

type Campo = 'description' | 'amount' | 'dayOfMonth' | 'origin' | 'startMonth' | 'end';

/**
 * Telas `/recorrencias/nova` e `/recorrencias/:id` (005 US1, US3; FR-001, FR-005–FR-007): tipo, descrição,
 * valor com calculadora, dia, carteira/cartão, tags, mês inicial, término e, só para Entrada/Saída, a regra de
 * mês de referência. Ao salvar uma edição pergunta "a partir de qual mês?"; Encerrar e Excluir confirmam.
 */
@Component({
  selector: 'app-recorrencia-form-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, AmountInput, TagPicker],
  templateUrl: './recorrencia-form.page.html',
  styleUrl: './recorrencia-form.page.scss',
})
export class RecorrenciaFormPage {
  private readonly recurrences = inject(RecurrencesService);
  private readonly wallets = inject(WalletsService);
  private readonly cards = inject(CardsService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');
  /** Uma chave por formulário, reaproveitada em reenvios (a criação é idempotente). */
  private readonly chaveIdempotencia = crypto.randomUUID();

  protected readonly tipos = RECURRENCE_KINDS;
  protected readonly terminos = RECURRENCE_END_TYPES;
  protected readonly regras = REFERENCE_MONTH_RULES;
  protected readonly descricaoMax = DESCRICAO_MAX;
  protected readonly ocorrenciasMax = OCORRENCIAS_MAX;
  protected readonly edicao = this.id !== null;

  protected readonly kind = signal<RecurrenceKind>('expense');
  protected readonly endType = signal<RecurrenceEndType>('none');
  protected readonly tagIds = signal<string[]>([]);

  protected readonly form = this.fb.group({
    description: [''],
    amount: [''],
    dayOfMonth: [today().slice(8, 10)],
    startMonth: [today().slice(0, 7)],
    endMonth: [''],
    occurrenceLimit: [''],
  });

  // Escolhas de lista ficam em signals: as opções chegam depois (HTTP) e `[selected]` acerta a marcação.
  protected readonly walletId = signal('');
  protected readonly creditCardId = signal('');
  protected readonly referenceMonthRule = signal<ReferenceMonthRule>('same_month');

  protected readonly recorrencia = signal<Recurrence | null>(null);
  protected readonly carteiras = signal<Opcao[]>([]);
  protected readonly cartoes = signal<Opcao[]>([]);
  protected readonly carregando = signal(this.edicao);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly erros = signal<Partial<Record<Campo, string>>>({});

  constructor() {
    this.carregarOpcoes();
    if (this.id) {
      this.recurrences
        .get(this.id)
        .pipe(takeUntilDestroyed())
        .subscribe({
          next: (r) => {
            this.preencher(r);
            this.carregando.set(false);
          },
          error: (error: unknown) => {
            this.carregando.set(false);
            this.erro.set(
              asProblem(error)?.code === 'not_found'
                ? 'Recorrência não encontrada.'
                : problemMessage(error),
            );
          },
        });
    }
  }

  protected escolherTipo(tipo: RecurrenceKind): void {
    if (this.edicao) return;
    this.kind.set(tipo);
  }

  protected escolherCarteira(id: string): void {
    this.walletId.set(id);
  }

  protected escolherCartao(id: string): void {
    this.creditCardId.set(id);
  }

  protected escolherRegra(regra: string): void {
    this.referenceMonthRule.set(regra as ReferenceMonthRule);
  }

  protected escolherTermino(tipo: RecurrenceEndType): void {
    this.endType.set(tipo);
  }

  protected salvar(): void {
    if (this.ocupado()) return;
    this.erro.set(null);
    this.info.set(null);
    const entrada = this.validar();
    if (!entrada) return;

    const atual = this.recorrencia();
    if (!atual || !this.id) {
      this.executar(
        this.recurrences.create(entrada, this.chaveIdempotencia),
        () => void this.router.navigateByUrl('/recorrencias'),
      );
      return;
    }
    this.perguntarMes(
      {
        titulo: 'Salvar alterações',
        mensagem:
          'As ocorrências previstas e não alteradas à mão, desse mês em diante, receberão as mudanças.',
        confirmar: 'Salvar',
        mesPadrao: today().slice(0, 7),
      },
      (fromMonth) =>
        this.executar(
          this.recurrences.update(this.id!, { ...entrada, fromMonth, version: atual.version }),
          (r) => {
            this.info.set(
              `Alterada a partir de ${fromMonth}: ${r.affectedCount} lançamento(s) atualizado(s).`,
            );
            this.recarregar();
          },
        ),
    );
  }

  protected encerrar(): void {
    const atual = this.recorrencia();
    if (!atual || this.ocupado()) return;
    this.erro.set(null);
    this.info.set(null);
    this.perguntarMes(
      {
        titulo: 'Encerrar recorrência',
        mensagem: `Encerrar "${atual.description}"? As ocorrências previstas e não alteradas, desse mês em diante, serão excluídas e nenhuma nova será criada.`,
        confirmar: 'Encerrar',
        mesPadrao: today().slice(0, 7),
      },
      (fromMonth) =>
        this.executar(this.recurrences.end(atual.id, fromMonth), (r) => {
          this.info.set(
            `Encerrada a partir de ${fromMonth}: ${r.deletedCount} lançamento(s) excluído(s).`,
          );
          this.recarregar();
        }),
    );
  }

  protected excluir(): void {
    const atual = this.recorrencia();
    if (!atual || this.ocupado()) return;
    this.erro.set(null);
    this.info.set(null);
    const data: ConfirmarData = {
      titulo: 'Excluir recorrência',
      mensagem: `Excluir "${atual.description}"? As ocorrências previstas a partir deste mês serão excluídas; as demais ficam como lançamentos comuns. Isso não pode ser desfeito.`,
      confirmar: 'Excluir',
    };
    this.dialog
      .open<ConfirmarDialog, ConfirmarData, ConfirmarResultado>(ConfirmarDialog, { data })
      .afterClosed()
      .subscribe((resultado) => {
        if (resultado === null || resultado === undefined) return;
        this.executar(
          this.recurrences.delete(atual.id),
          () => void this.router.navigateByUrl('/recorrencias'),
        );
      });
  }

  private perguntarMes(data: APartirDeData, aoConfirmar: (mes: string) => void): void {
    this.dialog
      .open<APartirDeDialog, APartirDeData, APartirDeResultado>(APartirDeDialog, { data })
      .afterClosed()
      .subscribe((mes) => {
        if (mes) aoConfirmar(mes);
      });
  }

  /** Confere os campos; devolve a entrada pronta ou `null` (com as mensagens em `erros`). */
  private validar(): RecurrenceInput | null {
    const v = this.form.getRawValue();
    const kind = this.kind();
    const endType = this.endType();
    const erros: Partial<Record<Campo, string>> = {};

    const descricao = v.description.trim();
    if (descricao === '') erros.description = 'Informe a descrição.';
    else if (descricao.length > DESCRICAO_MAX)
      erros.description = `A descrição pode ter até ${DESCRICAO_MAX} caracteres.`;

    const valor = parseValueExpression(v.amount, false);
    if (!valor.ok) erros.amount = valueExpressionMessages[valor.error];

    const dia = Number(v.dayOfMonth);
    if (!Number.isInteger(dia) || dia < 1 || dia > 31)
      erros.dayOfMonth = 'Informe um dia de 1 a 31.';

    const origem = kind === 'card_purchase' ? this.creditCardId() : this.walletId();
    if (!origem)
      erros.origin = kind === 'card_purchase' ? 'Escolha o cartão.' : 'Escolha a carteira.';

    if (!isYearMonth(v.startMonth)) erros.startMonth = 'Informe o mês inicial.';

    const limite = Number(v.occurrenceLimit);
    if (endType === 'until_month') {
      if (!isYearMonth(v.endMonth)) erros.end = 'Informe o mês final.';
      else if (isYearMonth(v.startMonth) && v.endMonth < v.startMonth)
        erros.end = 'O mês final não pode ser antes do mês inicial.';
    } else if (endType === 'after_count') {
      if (!Number.isInteger(limite) || limite < 1 || limite > OCORRENCIAS_MAX)
        erros.end = `Informe de 1 a ${OCORRENCIAS_MAX} ocorrências.`;
    }

    this.erros.set(erros);
    if (Object.keys(erros).length > 0) return null;

    return {
      kind,
      description: descricao,
      amount: v.amount.trim(),
      dayOfMonth: dia,
      walletId: kind === 'card_purchase' ? null : this.walletId(),
      creditCardId: kind === 'card_purchase' ? this.creditCardId() : null,
      tagIds: this.tagIds(),
      startMonth: v.startMonth,
      endType,
      endMonth: endType === 'until_month' ? v.endMonth : null,
      occurrenceLimit: endType === 'after_count' ? limite : null,
      referenceMonthRule: kind === 'card_purchase' ? null : this.referenceMonthRule(),
    };
  }

  private preencher(r: Recurrence): void {
    this.recorrencia.set(r);
    this.kind.set(r.kind);
    this.endType.set(r.endType);
    this.tagIds.set([...r.tagIds]);
    this.form.reset({
      description: r.description,
      amount: centavosParaCampo(r.amountCents),
      dayOfMonth: String(r.dayOfMonth),
      startMonth: r.startMonth,
      endMonth: r.endMonth ?? '',
      occurrenceLimit: r.occurrenceLimit === null ? '' : String(r.occurrenceLimit),
    });
    this.walletId.set(r.walletId ?? '');
    this.creditCardId.set(r.creditCardId ?? '');
    this.referenceMonthRule.set(r.referenceMonthRule ?? 'same_month');
    // Carteira/cartão arquivados da recorrência continuam escolhidos (a lista traz todos).
    this.garantirOpcoes();
  }

  private recarregar(): void {
    this.recurrences.get(this.id!).subscribe({
      next: (r) => this.preencher(r),
      error: (error: unknown) => this.erro.set(problemMessage(error)),
    });
  }

  private executar<T>(chamada: Observable<T>, aoConcluir: (resultado: T) => void): void {
    this.ocupado.set(true);
    this.erro.set(null);
    chamada.subscribe({
      next: (resultado) => {
        this.ocupado.set(false);
        aoConcluir(resultado);
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }

  private todasCarteiras: (Opcao & { archived: boolean })[] = [];
  private todosCartoes: (Opcao & { archived: boolean })[] = [];

  /** Carteiras e cartões ativos; os arquivados só aparecem se forem os da recorrência. */
  private carregarOpcoes(): void {
    this.wallets.list('all').subscribe({
      next: (lista) => {
        this.todasCarteiras = lista;
        if (!this.edicao && !this.walletId()) {
          const principal =
            lista.find((w) => w.isPrimary && !w.archived) ?? lista.find((w) => !w.archived);
          if (principal) this.walletId.set(principal.id);
        }
        this.garantirOpcoes();
      },
      error: (error: unknown) => this.erro.set(problemMessage(error)),
    });
    this.cards.list('all').subscribe({
      next: (lista) => {
        this.todosCartoes = lista;
        if (!this.edicao && !this.creditCardId()) {
          const primeiro = lista.find((c) => !c.archived);
          if (primeiro) this.creditCardId.set(primeiro.id);
        }
        this.garantirOpcoes();
      },
      error: (error: unknown) => this.erro.set(problemMessage(error)),
    });
  }

  private garantirOpcoes(): void {
    const atual = this.recorrencia();
    const filtrar = (
      lista: (Opcao & { archived: boolean })[],
      escolhido: string | null | undefined,
      sufixo: string,
    ) =>
      lista
        .filter((i) => !i.archived || i.id === escolhido)
        .map((i) => ({ id: i.id, name: i.archived ? `${i.name} (${sufixo})` : i.name }));
    this.carteiras.set(filtrar(this.todasCarteiras, atual?.walletId, 'arquivada'));
    this.cartoes.set(filtrar(this.todosCartoes, atual?.creditCardId, 'arquivado'));
  }
}
