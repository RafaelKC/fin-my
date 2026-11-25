import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { isYearMonth } from '../../core/money';

export interface APartirDeData {
  titulo: string;
  /** Texto de apoio ou de confirmação acima do campo. */
  mensagem?: string;
  confirmar: string;
  /** `AAAA-MM` sugerido (padrão: mês de hoje). */
  mesPadrao: string;
}

/** Resultado: `null` se cancelou; senão o mês `AAAA-MM` escolhido. */
export type APartirDeResultado = string | null;

/** Diálogo "A partir de qual mês?" para alterar ou encerrar uma recorrência (FR-006, FR-007). */
@Component({
  selector: 'app-a-partir-de-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule],
  template: `
    <h2 mat-dialog-title>{{ data.titulo }}</h2>
    <mat-dialog-content>
      @if (data.mensagem) {
        <p>{{ data.mensagem }}</p>
      }
      <label for="a-partir-de-mes">A partir de qual mês?</label>
      <input
        id="a-partir-de-mes"
        type="month"
        class="mes"
        data-testid="a-partir-de-mes"
        [value]="mes()"
        (input)="mes.set($any($event.target).value)"
      />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button type="button" class="botao" data-testid="cancelar" (click)="ref.close(null)">
        Cancelar
      </button>
      <button
        type="button"
        class="botao principal"
        data-testid="confirmar"
        [disabled]="!valido()"
        (click)="ref.close(mes())"
      >
        {{ data.confirmar }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .mes {
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
    .botao {
      min-width: 44px;
      min-height: 44px;
      padding: 0 16px;
      font: var(--fonte-rotulo-g);
      color: var(--primary);
      background: transparent;
      border: 0;
      border-radius: 24px;
      cursor: pointer;
    }
    .botao.principal {
      color: var(--primary-foreground);
      background: var(--primary);
    }
    .botao:disabled {
      opacity: 0.6;
      cursor: default;
    }
  `,
})
export class APartirDeDialog {
  protected readonly data = inject<APartirDeData>(MAT_DIALOG_DATA);
  protected readonly ref = inject<MatDialogRef<APartirDeDialog, APartirDeResultado>>(MatDialogRef);
  protected readonly mes = signal(this.data.mesPadrao);
  protected readonly valido = computed(() => isYearMonth(this.mes()));
}
