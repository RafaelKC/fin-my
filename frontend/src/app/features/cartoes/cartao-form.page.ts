import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable, forkJoin, of } from 'rxjs';
import { startWith } from 'rxjs/operators';
import { asProblem, problemMessage } from '../../core/errors/problem-details';
import { formatBrl, parseValueExpression, valueExpressionMessages } from '../../core/money';
import { centavosParaCampo } from '../carteiras/carteira-form.page';
import { ConfirmarDialog, ConfirmarData, ConfirmarResultado } from '../carteiras/confirmar.dialog';
import { Wallet } from '../carteiras/wallet.models';
import { WalletsService } from '../carteiras/wallets.service';
import { CARD_COLORS, CreditCard, CreditCardInput, NOME_CARTAO_MAX } from './card.models';
import { CardsService } from './cards.service';

const TEM_COMPRAS = 'Este cartão tem compras ou pagamentos. Você pode arquivá-lo.';

/** Telas `/cartoes/novo` e `/cartoes/:id/editar` (US1; FR-001, RT-11). */
@Component({
  selector: 'app-cartao-form-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './cartao-form.page.html',
  styleUrl: './cartao-form.page.scss',
})
export class CartaoFormPage {
  private readonly cards = inject(CardsService);
  private readonly wallets = inject(WalletsService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');

  protected readonly cores = CARD_COLORS;
  protected readonly nomeMax = NOME_CARTAO_MAX;
  protected readonly edicao = this.id !== null;

  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(NOME_CARTAO_MAX)]],
    closingDay: [18 as number | null, [Validators.required, Validators.min(1), Validators.max(31)]],
    dueDay: [25 as number | null, [Validators.required, Validators.min(1), Validators.max(31)]],
    paymentWalletId: ['', [Validators.required]],
    color: [CARD_COLORS[0].value],
    limit: [''],
  });

  protected readonly cartao = signal<CreditCard | null>(null);
  protected readonly carteiras = signal<Wallet[]>([]);
  protected readonly carregando = signal(true);
  protected readonly tentou = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly erroNome = signal<string | null>(null);
  protected readonly erroCarteira = signal<string | null>(null);

  private readonly limiteTexto = toSignal(
    this.form.controls.limit.valueChanges.pipe(startWith(this.form.controls.limit.value)),
    { initialValue: '' },
  );

  /** Pré-visualização "= R$ 3.000,00", "Sem limite" ou a mensagem de erro do parser (RT-05). */
  protected previaLimite(): { ok: boolean; texto: string } {
    const texto = this.limiteTexto().trim();
    if (texto === '') return { ok: true, texto: 'Sem limite' };
    const r = parseValueExpression(texto, true);
    if (!r.ok) return { ok: false, texto: valueExpressionMessages[r.error] };
    if (r.cents < 0) return { ok: false, texto: 'O limite não pode ser negativo.' };
    return { ok: true, texto: `= ${formatBrl(r.cents)}` };
  }

  constructor() {
    forkJoin({
      carteiras: this.wallets.list('all'),
      cartao: this.id ? this.cards.get(this.id) : of(null),
    })
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: ({ carteiras, cartao }) => {
          // Só carteiras ativas (FR-001); a de pagamento atual fica mesmo se arquivada depois.
          const ativas = carteiras.filter((w) => !w.archived || w.id === cartao?.paymentWalletId);
          this.carteiras.set(ativas);
          if (cartao) {
            this.cartao.set(cartao);
            this.form.reset({
              name: cartao.name,
              closingDay: cartao.closingDay,
              dueDay: cartao.dueDay,
              paymentWalletId: cartao.paymentWalletId,
              color: cartao.color,
              limit:
                cartao.limitCents === null || cartao.limitCents === undefined
                  ? ''
                  : centavosParaCampo(cartao.limitCents),
            });
          } else {
            const padrao = ativas.find((w) => w.isPrimary) ?? ativas[0];
            if (padrao) this.form.controls.paymentWalletId.setValue(padrao.id);
          }
          this.carregando.set(false);
        },
        error: (error: unknown) => {
          this.carregando.set(false);
          this.erro.set(
            asProblem(error)?.code === 'not_found'
              ? 'Cartão não encontrado.'
              : problemMessage(error),
          );
        },
      });
  }

  protected erroDoNome(): string | null {
    const c = this.form.controls.name;
    if ((this.tentou() || c.touched) && c.errors) {
      return c.errors['required']
        ? 'Informe o nome do cartão.'
        : `O nome pode ter até ${NOME_CARTAO_MAX} caracteres.`;
    }
    return this.erroNome();
  }

  protected erroDoDia(campo: 'closingDay' | 'dueDay'): string | null {
    const c = this.form.controls[campo];
    if ((this.tentou() || c.touched) && c.errors) {
      return 'Informe um dia de 1 a 31.';
    }
    return null;
  }

  protected erroDaCarteira(): string | null {
    const c = this.form.controls.paymentWalletId;
    if ((this.tentou() || c.touched) && c.errors) return 'Escolha a carteira de pagamento.';
    return this.erroCarteira();
  }

  protected escolherCor(cor: string): void {
    this.form.controls.color.setValue(cor);
  }

  protected salvar(): void {
    this.tentou.set(true);
    this.erro.set(null);
    this.erroNome.set(null);
    this.erroCarteira.set(null);
    if (this.ocupado()) return;
    if (this.form.invalid || !this.previaLimite().ok) return;

    const v = this.form.getRawValue();
    const limite = v.limit.trim();
    const input: CreditCardInput = {
      name: v.name.trim(),
      closingDay: Number(v.closingDay),
      dueDay: Number(v.dueDay),
      paymentWalletId: v.paymentWalletId,
      color: v.color,
      limit: limite === '' ? null : limite,
    };
    const atual = this.cartao();
    const chamada =
      atual && this.id
        ? this.cards.update(this.id, input, atual.version)
        : this.cards.create(input);
    this.executar(chamada, () => void this.router.navigateByUrl('/cartoes'));
  }

  protected arquivar(): void {
    const atual = this.cartao();
    if (!atual || this.ocupado()) return;
    this.executar(this.cards.archive(atual.id), () => void this.router.navigateByUrl('/cartoes'));
  }

  protected desarquivar(): void {
    const atual = this.cartao();
    if (!atual || this.ocupado()) return;
    this.executar(this.cards.unarchive(atual.id), () => this.recarregar());
  }

  protected excluir(): void {
    const atual = this.cartao();
    if (!atual || this.ocupado()) return;
    this.erro.set(null);
    if (atual.hasPurchases) {
      this.erro.set(TEM_COMPRAS);
      return;
    }
    const data: ConfirmarData = {
      titulo: 'Excluir cartão',
      mensagem: `Excluir "${atual.name}" definitivamente? Isso não pode ser desfeito.`,
      confirmar: 'Excluir',
    };
    this.dialog
      .open<ConfirmarDialog, ConfirmarData, ConfirmarResultado>(ConfirmarDialog, { data })
      .afterClosed()
      .subscribe((resultado) => {
        if (resultado === null || resultado === undefined) return;
        this.executar(
          this.cards.delete(atual.id),
          () => void this.router.navigateByUrl('/cartoes'),
        );
      });
  }

  private recarregar(): void {
    this.cards.get(this.id!).subscribe({
      next: (c) => this.cartao.set(c),
      error: (error: unknown) => this.erro.set(problemMessage(error)),
    });
  }

  private executar<T>(chamada: Observable<T>, aoConcluir: () => void): void {
    this.ocupado.set(true);
    this.erro.set(null);
    chamada.subscribe({
      next: () => {
        this.ocupado.set(false);
        aoConcluir();
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        const problem = asProblem(error);
        if (problem?.code === 'credit_card.name_taken') {
          this.erroNome.set(problem.detail || 'Já existe um cartão com esse nome.');
          return;
        }
        if (problem?.code === 'wallet.archived' || problem?.code === 'wallet.required') {
          this.erroCarteira.set(problem.detail || 'Escolha outra carteira de pagamento.');
          return;
        }
        this.erro.set(problemMessage(error));
      },
    });
  }
}
