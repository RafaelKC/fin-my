import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';

export interface EscopoOpcao<T extends string> {
  valor: T;
  rotulo: string;
}

export interface EscopoData<T extends string> {
  titulo: string;
  mensagem?: string;
  opcoes: EscopoOpcao<T>[];
}

/** Pergunta de escopo em parcelas ("Só esta / Esta e as seguintes / Todas"; FR-016, FR-028). Resultado: o valor ou `null`. */
@Component({
  selector: 'app-escopo-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule],
  template: `
    <h2 mat-dialog-title>{{ data.titulo }}</h2>
    @if (data.mensagem) {
      <mat-dialog-content>
        <p>{{ data.mensagem }}</p>
      </mat-dialog-content>
    }
    <mat-dialog-actions class="acoes">
      @for (o of data.opcoes; track o.valor) {
        <button
          type="button"
          class="botao"
          [attr.data-testid]="'escopo-' + o.valor"
          (click)="ref.close(o.valor)"
        >
          {{ o.rotulo }}
        </button>
      }
      <button type="button" class="botao" (click)="ref.close(null)">Cancelar</button>
    </mat-dialog-actions>
  `,
  styles: `
    .acoes {
      flex-direction: column;
      align-items: stretch;
    }
    .botao {
      min-height: 44px;
      padding: 0 16px;
      font: var(--fonte-rotulo-g);
      color: var(--primary);
      background: transparent;
      border: 0;
      border-radius: 24px;
      cursor: pointer;
    }
  `,
})
export class EscopoDialog<T extends string = string> {
  protected readonly data = inject<EscopoData<T>>(MAT_DIALOG_DATA);
  protected readonly ref = inject<MatDialogRef<EscopoDialog<T>, T | null>>(MatDialogRef);
}
