import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { BrlPipe } from '../../core/money';
import { formatarData } from '../mes/mes-logic';
import { PlanningCellItem } from './planning.models';

export interface ItensCelulaData {
  titulo: string;
  items: PlanningCellItem[];
}

/** Resultado: `null` se fechou; senão o id do lançamento a editar. */
export type ItensCelulaResultado = string | null;

const SITUACAO: Record<string, string> = { planned: 'Previsto', settled: 'Efetivado' };

/** Lista de lançamentos de uma célula não editável (US1.5, FR-003): edição individual pelo formulário rápido. */
@Component({
  selector: 'app-itens-celula-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, BrlPipe],
  styleUrl: './dialogos.scss',
  template: `
    <h2 mat-dialog-title>{{ data.titulo }}</h2>
    <mat-dialog-content>
      <p class="nota" data-testid="itens-aviso">
        Esta célula tem mais de um lançamento ou algum já efetivado. Edite cada lançamento
        individualmente.
      </p>
      <ul class="itens" data-testid="itens-celula">
        @for (i of data.items; track i.id) {
          <li data-testid="item-celula">
            <span class="descricao">{{ i.description }}</span>
            <span class="nota">{{ data_(i.date) }} · {{ situacao(i.status) }}</span>
            <strong>{{ i.amountCents | brl }}</strong>
            <button type="button" class="botao" data-testid="item-editar" (click)="ref.close(i.id)">
              Editar
            </button>
          </li>
        }
      </ul>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button type="button" class="botao" data-testid="fechar" (click)="ref.close(null)">
        Fechar
      </button>
    </mat-dialog-actions>
  `,
})
export class ItensCelulaDialog {
  protected readonly data = inject<ItensCelulaData>(MAT_DIALOG_DATA);
  protected readonly ref =
    inject<MatDialogRef<ItensCelulaDialog, ItensCelulaResultado>>(MatDialogRef);
  protected readonly data_ = formatarData;
  protected readonly situacao = (s: string): string => SITUACAO[s] ?? s;
}
