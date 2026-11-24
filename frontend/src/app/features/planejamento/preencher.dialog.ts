import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { monthShortLabel, parseValueExpression, valueExpressionMessages } from '../../core/money';
import { AmountInput } from '../../shared/amount-input/amount-input';

export interface PreencherData {
  titulo: string;
  /** Meses `AAAA-MM` do ano mostrado. */
  meses: string[];
  /** Meses que já têm valor (só informativo: o servidor os pula). */
  mesesComValor: string[];
}

export interface PreencherResultado {
  amount: string;
  months: string[];
}

/** Diálogo "Preencher meses" (US1.8, US2.5): um valor e os meses a receber. */
@Component({
  selector: 'app-preencher-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, ReactiveFormsModule, AmountInput],
  styleUrl: './dialogos.scss',
  template: `
    <h2 mat-dialog-title>{{ data.titulo }}</h2>
    <mat-dialog-content>
      <app-amount-input [formControl]="valor" />
      <fieldset class="meses" data-testid="preencher-meses">
        <legend>Meses</legend>
        @for (m of data.meses; track m) {
          <label>
            <input
              type="checkbox"
              [attr.data-mes]="m"
              [checked]="escolhidos().has(m)"
              (change)="alternar(m)"
            />
            {{ rotulo(m) }}
          </label>
        }
      </fieldset>
      <p class="nota">Meses que já têm valor não são alterados.</p>
      @if (erro(); as mensagem) {
        <p class="erro" role="alert">{{ mensagem }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button type="button" class="botao" data-testid="cancelar" (click)="ref.close(null)">
        Cancelar
      </button>
      <button
        type="button"
        class="botao principal"
        data-testid="preencher-confirmar"
        [disabled]="escolhidos().size === 0"
        (click)="confirmar()"
      >
        Preencher
      </button>
    </mat-dialog-actions>
  `,
})
export class PreencherDialog {
  protected readonly data = inject<PreencherData>(MAT_DIALOG_DATA);
  protected readonly ref =
    inject<MatDialogRef<PreencherDialog, PreencherResultado | null>>(MatDialogRef);

  protected readonly valor = new FormControl('', { nonNullable: true });
  protected readonly escolhidos = signal<ReadonlySet<string>>(new Set(this.data.meses));
  protected readonly erro = signal<string | null>(null);
  protected readonly rotulo = (m: string): string => monthShortLabel(m).slice(0, 3);
  protected readonly todos = computed(() => this.escolhidos().size === this.data.meses.length);

  protected alternar(mes: string): void {
    const novo = new Set(this.escolhidos());
    if (!novo.delete(mes)) novo.add(mes);
    this.escolhidos.set(novo);
  }

  protected confirmar(): void {
    const analise = parseValueExpression(this.valor.value, false);
    if (!analise.ok) {
      this.erro.set(valueExpressionMessages[analise.error]);
      return;
    }
    this.ref.close({
      amount: this.valor.value.trim(),
      months: this.data.meses.filter((m) => this.escolhidos().has(m)),
    });
  }
}
