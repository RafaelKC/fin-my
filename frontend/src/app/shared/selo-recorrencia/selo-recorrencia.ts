import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/**
 * Marca de ocorrência de recorrência nas listas (005): ícone quando o lançamento veio de uma
 * recorrência e "alterada" quando foi editado à mão (`recurrenceModified`, FR-005).
 */
@Component({
  selector: 'app-selo-recorrencia',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule],
  template: `@if (recorrenciaId()) {
    <span class="selo-recorrencia" data-testid="selo-recorrencia">
      <mat-icon fontSet="material-symbols-outlined" aria-label="Recorrência">repeat</mat-icon>
      @if (alterada()) {
        <span class="alterada" data-testid="selo-alterada">alterada</span>
      }
    </span>
  }`,
  styles: `
    .selo-recorrencia {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      margin-left: 8px;
      vertical-align: middle;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-label-small);
    }
    mat-icon {
      width: 16px;
      height: 16px;
      font-size: 16px;
      line-height: 16px;
    }
    .alterada {
      padding: 0 6px;
      border-radius: 4px;
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }
  `,
})
export class SeloRecorrencia {
  readonly recorrenciaId = input<string | null | undefined>();
  readonly alterada = input<boolean | null | undefined>();
}
