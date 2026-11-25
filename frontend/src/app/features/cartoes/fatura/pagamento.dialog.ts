import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { parseValueExpression, today } from '../../../core/money';
import { asProblem, problemMessage } from '../../../core/errors/problem-details';
import { AmountInput } from '../../../shared/amount-input/amount-input';
import { Wallet } from '../../carteiras/wallet.models';
import { centavosParaExpressao, novaChaveIdempotencia } from '../../lancamento/lancamento-logic';
import { InvoicePayment } from './invoice.models';
import { InvoicesService } from './invoices.service';
import { DADOS_DIALOGO, DialogoRef } from '@/shared/ui/dialogo';
import { ZardButtonComponent } from '@/shared/components/button';
import { CampoDirective } from '@/shared/ui/campo.directive';
import { ZardDialogFooterComponent, ZardDialogTitleComponent } from '@/shared/components/dialog';
import { ZardNativeSelectDirective } from '@/shared/components/native-select';

export interface PagamentoDialogData {
  cardId: string;
  month: string;
  carteiras: Wallet[];
  /** Restante da fatura: valor padrão de um pagamento novo (FR-017). */
  remainingCents: number;
  /** Carteira de pagamento do cartão: padrão de um pagamento novo. */
  carteiraPadraoId: string;
  /** Presente = edição (FR-018). */
  pagamento?: InvoicePayment;
}

/** Pagar a fatura (valor, data, carteira; FR-017) ou editar um pagamento (FR-018). Resultado: `true` se salvou. */
@Component({
  selector: 'app-pagamento-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ZardButtonComponent,
    CampoDirective,
    ZardDialogFooterComponent,
    ZardDialogTitleComponent,
    ZardNativeSelectDirective,
    FormsModule,
    AmountInput,
  ],
  template: `
    <h2 z-dialog-title>{{ data.pagamento ? 'Editar pagamento' : 'Pagar fatura' }}</h2>
    <div class="dialogo-corpo">
      <app-amount-input
        label="Valor"
        [ngModel]="valor()"
        (ngModelChange)="valor.set($event)"
        [ngModelOptions]="{ standalone: true }"
      />
      <label for="pagamento-data">Data</label>
      <input
        appCampo
        id="pagamento-data"
        class="campo"
        type="date"
        [value]="dataPagamento()"
        (input)="dataPagamento.set($any($event.target).value)"
      />
      <label for="pagamento-carteira">Carteira</label>
      <select
        z-native-select
        id="pagamento-carteira"
        class="campo"
        [value]="carteira()"
        (change)="carteira.set($any($event.target).value)"
      >
        @for (w of data.carteiras; track w.id) {
          <option [value]="w.id" [selected]="w.id === carteira()">{{ w.name }}</option>
        }
      </select>
      @if (erro(); as mensagem) {
        <p class="erro" role="alert" data-testid="erro-pagamento">{{ mensagem }}</p>
      }
    </div>
    <footer z-dialog-footer>
      <button z-button zType="outline" type="button" class="botao" (click)="ref.close(false)">
        Cancelar
      </button>
      <button
        z-button
        type="button"
        class="botao principal"
        data-testid="confirmar-pagamento"
        [disabled]="ocupado()"
        (click)="salvar()"
      >
        {{ data.pagamento ? 'Salvar' : 'Pagar' }}
      </button>
    </footer>
  `,
  styles: `
    label {
      display: block;
      margin-top: 12px;
    }
    .campo {
      box-sizing: border-box;
      width: 100%;
      min-height: 48px;
      margin-top: 4px;
      padding: 8px 12px;
      font: var(--fonte-corpo-g);
      color: inherit;
      background: transparent;
      border: 1px solid var(--border-forte);
      border-radius: 8px;
    }
  `,
})
export class PagamentoDialog {
  protected readonly data = inject<PagamentoDialogData>(DADOS_DIALOGO);
  protected readonly ref = inject<DialogoRef<PagamentoDialog, boolean>>(DialogoRef);
  private readonly invoices = inject(InvoicesService);

  protected readonly valor = signal(
    centavosParaExpressao(
      this.data.pagamento?.amountCents ?? Math.max(this.data.remainingCents, 0),
    ),
  );
  protected readonly dataPagamento = signal(this.data.pagamento?.date ?? today());
  protected readonly carteira = signal(
    this.data.pagamento?.walletId ?? this.data.carteiraPadraoId ?? this.data.carteiras[0]?.id ?? '',
  );
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);
  private readonly chave = novaChaveIdempotencia();

  protected salvar(): void {
    if (this.ocupado()) return;
    const valor = parseValueExpression(this.valor());
    if (!valor.ok) {
      this.erro.set('Informe um valor válido.');
      return;
    }
    if (this.dataPagamento() === '') {
      this.erro.set('Informe a data.');
      return;
    }
    if (this.carteira() === '') {
      this.erro.set('Escolha uma carteira.');
      return;
    }
    const corpo = {
      amount: this.valor().trim(),
      date: this.dataPagamento(),
      walletId: this.carteira(),
    };
    this.ocupado.set(true);
    this.erro.set(null);
    const pedido: Observable<unknown> = this.data.pagamento
      ? this.invoices.updatePayment(this.data.pagamento.id, {
          ...corpo,
          version: this.data.pagamento.version,
        })
      : this.invoices.pay(this.data.cardId, this.data.month, corpo, this.chave);
    pedido.subscribe({
      next: () => this.ref.close(true),
      error: (error: unknown) => {
        this.ocupado.set(false);
        this.erro.set(
          asProblem(error)?.code === 'concurrency_conflict'
            ? 'Este pagamento foi alterado em outro aparelho. Feche e abra de novo.'
            : problemMessage(error),
        );
      },
    });
  }
}
