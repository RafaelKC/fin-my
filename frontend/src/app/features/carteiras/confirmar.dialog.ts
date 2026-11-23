import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Wallet } from './wallet.models';

export interface ConfirmarData {
  titulo: string;
  mensagem: string;
  confirmar: string;
  /** Quando presente, a pessoa precisa escolher uma destas como nova principal. */
  escolherPrincipal?: Wallet[];
}

/** Resultado: `null` se cancelou; senão o id escolhido (ou `''` se não havia escolha). */
export type ConfirmarResultado = string | null;

@Component({
  selector: 'app-confirmar-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule],
  template: `
    <h2 mat-dialog-title>{{ data.titulo }}</h2>
    <mat-dialog-content>
      <p>{{ data.mensagem }}</p>
      @if (data.escolherPrincipal; as opcoes) {
        <label for="nova-principal">Nova carteira principal</label>
        <select
          id="nova-principal"
          class="escolha"
          [value]="escolhida()"
          (change)="escolhida.set($any($event.target).value)"
        >
          <option value="">Escolha uma carteira</option>
          @for (w of opcoes; track w.id) {
            <option [value]="w.id">{{ w.name }}</option>
          }
        </select>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button type="button" class="botao" (click)="ref.close(null)">Cancelar</button>
      <button
        type="button"
        class="botao principal"
        data-testid="confirmar"
        [disabled]="data.escolherPrincipal && !escolhida()"
        (click)="ref.close(escolhida())"
      >
        {{ data.confirmar }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .escolha {
      box-sizing: border-box;
      width: 100%;
      min-height: 48px;
      margin-top: 4px;
      padding: 8px 12px;
      font: var(--mat-sys-body-large);
      color: inherit;
      background: transparent;
      border: 1px solid var(--mat-sys-outline);
      border-radius: 8px;
    }
    .botao {
      min-width: 44px;
      min-height: 44px;
      padding: 0 16px;
      font: var(--mat-sys-label-large);
      color: var(--mat-sys-primary);
      background: transparent;
      border: 0;
      border-radius: 24px;
      cursor: pointer;
    }
    .botao.principal {
      color: var(--mat-sys-on-primary);
      background: var(--mat-sys-primary);
    }
    .botao:disabled {
      opacity: 0.6;
      cursor: default;
    }
  `,
})
export class ConfirmarDialog {
  protected readonly data = inject<ConfirmarData>(MAT_DIALOG_DATA);
  protected readonly ref = inject<MatDialogRef<ConfirmarDialog, ConfirmarResultado>>(MatDialogRef);
  protected readonly escolhida = signal('');
}
