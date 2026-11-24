import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { parseValueExpression, valueExpressionMessages } from '../../core/money';
import { AmountInput } from '../../shared/amount-input/amount-input';

export interface CelulaData {
  titulo: string;
  /** Valor atual no formato do campo (`1.200,00` sem o ponto de milhar → `1200,00`); vazio se não há. */
  valorInicial: string;
}

/** Resultado: `null` se cancelou; senão o texto do valor (`''` apaga a célula). */
export type CelulaResultado = string | null;

/** Edição de célula por diálogo (celular, research R5): o campo com calculadora e "Limpar". */
@Component({
  selector: 'app-celula-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, ReactiveFormsModule, AmountInput],
  styleUrl: './dialogos.scss',
  template: `
    <h2 mat-dialog-title>{{ data.titulo }}</h2>
    <mat-dialog-content>
      <app-amount-input [formControl]="valor" />
      @if (erro(); as mensagem) {
        <p class="erro" role="alert">{{ mensagem }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (data.valorInicial !== '') {
        <button type="button" class="botao" data-testid="celula-limpar" (click)="ref.close('')">
          Limpar
        </button>
      }
      <button type="button" class="botao" data-testid="cancelar" (click)="ref.close(null)">
        Cancelar
      </button>
      <button type="button" class="botao principal" data-testid="celula-salvar" (click)="salvar()">
        Salvar
      </button>
    </mat-dialog-actions>
  `,
})
export class CelulaDialog {
  protected readonly data = inject<CelulaData>(MAT_DIALOG_DATA);
  protected readonly ref = inject<MatDialogRef<CelulaDialog, CelulaResultado>>(MatDialogRef);
  protected readonly valor = new FormControl(this.data.valorInicial, { nonNullable: true });
  protected readonly erro = signal<string | null>(null);

  protected salvar(): void {
    const texto = this.valor.value.trim();
    if (texto === '') {
      this.ref.close('');
      return;
    }
    const analise = parseValueExpression(texto, false);
    if (!analise.ok) {
      this.erro.set(valueExpressionMessages[analise.error]);
      return;
    }
    this.ref.close(texto);
  }
}
