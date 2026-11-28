import { Banco } from '../../shared/bancos/bancos';
import { BancoPicker } from '../../shared/bancos/banco-picker';
import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { startWith } from 'rxjs/operators';
import { asProblem, problemMessage } from '../../core/errors/problem-details';
import {
  BrlPipe,
  formatBrl,
  parseValueExpression,
  today,
  valueExpressionMessages,
} from '../../core/money';
import { ConfirmarDialog, ConfirmarData, ConfirmarResultado } from './confirmar.dialog';
import {
  NOME_CARTEIRA_MAX,
  WALLET_COLORS,
  WALLET_TYPES,
  Wallet,
  WalletInput,
} from './wallet.models';
import { WalletsService } from './wallets.service';
import { Dialogo } from '@/shared/ui/dialogo';
import { ZardButtonComponent } from '@/shared/components/button';
import { CampoDirective } from '@/shared/ui/campo.directive';
import { ZardNativeSelectDirective } from '@/shared/components/native-select';

const SO_ATIVA = 'Cadastre outra carteira antes de arquivar esta: ela é a única ativa.';
const TEM_LANCAMENTOS = 'Esta carteira tem lançamentos. Você pode arquivá-la.';

/** `500,00` / `-150,00` para o campo de saldo inicial a partir dos centavos. */
export function centavosParaCampo(cents: number): string {
  return formatBrl(cents).replace('R$ ', '').replace('.', '');
}

/** Telas `/carteiras/nova` e `/carteiras/:id` (US1; FR-001, FR-002, FR-004, RT-11). */
@Component({
  selector: 'app-carteira-form-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BancoPicker,
    ZardButtonComponent,
    CampoDirective,
    ZardNativeSelectDirective,
    ReactiveFormsModule,
    RouterLink,
    BrlPipe,
  ],
  templateUrl: './carteira-form.page.html',
  styleUrl: './carteira-form.page.scss',
})
export class CarteiraFormPage {
  private readonly wallets = inject(WalletsService);
  private readonly router = inject(Router);
  private readonly dialog = inject(Dialogo);
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');

  /** No guia de primeiros passos (006 US3) o formulário é embutido: ao salvar, avisa em vez de navegar. */
  readonly embutido = input(false);
  readonly salvou = output<void>();

  protected readonly tipos = WALLET_TYPES;
  protected readonly cores = WALLET_COLORS;
  protected readonly nomeMax = NOME_CARTEIRA_MAX;
  protected readonly edicao = this.id !== null;

  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(NOME_CARTEIRA_MAX)]],
    type: ['checking' as WalletInput['type']],
    color: [WALLET_COLORS[0].value],
    bank: [null as string | null],
    initialBalance: ['0', [Validators.required]],
    initialBalanceDate: [today(), [Validators.required]],
  });

  protected readonly carteira = signal<Wallet | null>(null);
  protected readonly carregando = signal(this.edicao);
  protected readonly tentou = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly erroNome = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);

  private readonly saldoTexto = toSignal(
    this.form.controls.initialBalance.valueChanges.pipe(
      startWith(this.form.controls.initialBalance.value),
    ),
    { initialValue: '0' },
  );

  /** Pré-visualização "= R$ 102,20" ou a mensagem de erro do parser (RT-05). */
  protected previa(): { ok: boolean; texto: string } {
    const r = parseValueExpression(this.saldoTexto(), true);
    return r.ok
      ? { ok: true, texto: `= ${formatBrl(r.cents)}` }
      : { ok: false, texto: valueExpressionMessages[r.error] };
  }

  constructor() {
    if (this.id) {
      this.wallets
        .get(this.id)
        .pipe(takeUntilDestroyed())
        .subscribe({
          next: (w) => {
            this.carteira.set(w);
            this.form.reset({
              name: w.name,
              type: w.type,
              color: w.color,
              bank: w.bank ?? null,
              initialBalance: centavosParaCampo(w.initialBalanceCents),
              initialBalanceDate: w.initialBalanceDate,
            });
            this.carregando.set(false);
          },
          error: (error: unknown) => {
            this.carregando.set(false);
            this.erro.set(
              asProblem(error)?.code === 'not_found'
                ? 'Carteira não encontrada.'
                : problemMessage(error),
            );
          },
        });
    }
  }

  protected erroDoNome(): string | null {
    const c = this.form.controls.name;
    if ((this.tentou() || c.touched) && c.errors) {
      return c.errors['required']
        ? 'Informe o nome da carteira.'
        : `O nome pode ter até ${NOME_CARTEIRA_MAX} caracteres.`;
    }
    return this.erroNome();
  }

  /** Escolher o banco sugere a cor da marca e, se o nome estiver vazio, o nome do banco. */
  protected escolherBanco(banco: Banco | null): void {
    this.form.controls.bank.setValue(banco?.codigo ?? null);
    if (!banco) return;
    this.form.controls.color.setValue(banco.cor);
    if (!this.form.controls.name.value.trim()) this.form.controls.name.setValue(banco.nome);
  }

  protected escolherCor(cor: string): void {
    this.form.controls.color.setValue(cor);
  }

  protected salvar(): void {
    this.tentou.set(true);
    this.erro.set(null);
    this.erroNome.set(null);
    if (this.ocupado()) return;
    if (this.form.invalid || !this.previa().ok) return;

    const v = this.form.getRawValue();
    const input: WalletInput = {
      ...v,
      name: v.name.trim(),
      initialBalance: v.initialBalance.trim(),
    };
    const atual = this.carteira();
    const chamada =
      atual && this.id
        ? this.wallets.update(this.id, input, atual.version)
        : this.wallets.create(input);
    this.executar(chamada, () => this.concluir());
  }

  private concluir(): void {
    if (this.embutido()) this.salvou.emit();
    else void this.router.navigateByUrl('/carteiras');
  }

  protected tornarPrincipal(): void {
    this.executar(this.wallets.makePrimary(this.id!), () => this.recarregar());
  }

  protected desarquivar(): void {
    this.executar(this.wallets.unarchive(this.id!), () => this.recarregar());
  }

  protected arquivar(): void {
    const atual = this.carteira();
    if (!atual || this.ocupado()) return;
    this.erro.set(null);
    this.info.set(null);
    if (!atual.isPrimary) {
      this.executar(
        this.wallets.archive(atual.id),
        () => void this.router.navigateByUrl('/carteiras'),
      );
      return;
    }
    this.outrasAtivas((outras) => {
      if (outras.length === 0) {
        this.erro.set(SO_ATIVA);
        return;
      }
      this.confirmar(
        {
          titulo: 'Arquivar carteira principal',
          mensagem: `"${atual.name}" é a carteira principal. Escolha outra carteira para ser a principal antes de arquivar.`,
          confirmar: 'Arquivar',
          escolherPrincipal: outras,
        },
        (novaId) =>
          this.executar(
            this.wallets.archive(atual.id, novaId),
            () => void this.router.navigateByUrl('/carteiras'),
          ),
      );
    });
  }

  protected excluir(): void {
    const atual = this.carteira();
    if (!atual || this.ocupado()) return;
    this.erro.set(null);
    this.info.set(null);
    if (atual.hasEntries) {
      this.erro.set(TEM_LANCAMENTOS);
      return;
    }
    const excluirCom = (novaId?: string) =>
      this.executar(
        this.wallets.delete(atual.id, novaId || undefined),
        () => void this.router.navigateByUrl('/carteiras'),
      );
    const base = {
      titulo: 'Excluir carteira',
      confirmar: 'Excluir',
    };
    if (!atual.isPrimary) {
      this.confirmar(
        {
          ...base,
          mensagem: `Excluir "${atual.name}" definitivamente? Isso não pode ser desfeito.`,
        },
        excluirCom,
      );
      return;
    }
    this.outrasAtivas((outras) =>
      this.confirmar(
        {
          ...base,
          mensagem:
            `Excluir "${atual.name}" definitivamente? Isso não pode ser desfeito.` +
            (outras.length > 0 ? ' Ela é a principal: escolha outra para ser a principal.' : ''),
          escolherPrincipal: outras.length > 0 ? outras : undefined,
        },
        excluirCom,
      ),
    );
  }

  private outrasAtivas(depois: (outras: Wallet[]) => void): void {
    this.ocupado.set(true);
    this.wallets.list('false').subscribe({
      next: (lista) => {
        this.ocupado.set(false);
        depois(lista.filter((w) => w.id !== this.id));
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }

  private confirmar(data: ConfirmarData, aoConfirmar: (escolhida: string) => void): void {
    this.dialog
      .open<ConfirmarDialog, ConfirmarData, ConfirmarResultado>(ConfirmarDialog, { data })
      .afterClosed()
      .subscribe((resultado) => {
        if (resultado !== null && resultado !== undefined) aoConfirmar(resultado);
      });
  }

  private recarregar(): void {
    this.wallets.get(this.id!).subscribe({
      next: (w) => this.carteira.set(w),
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
        if (problem?.code === 'wallet.name_taken') {
          this.erroNome.set(problem.detail || 'Já existe uma carteira com esse nome.');
          return;
        }
        this.erro.set(problemMessage(error));
      },
    });
  }
}
