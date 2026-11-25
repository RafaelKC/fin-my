import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Icone } from '@/shared/ui/icone';

/**
 * Marca de ocorrência de recorrência nas listas (005): ícone quando o lançamento veio de uma
 * recorrência e "alterada" quando foi editado à mão (`recurrenceModified`, FR-005).
 */
@Component({
  selector: 'app-selo-recorrencia',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icone],
  template: `@if (recorrenciaId()) {
    <span class="selo-recorrencia" data-testid="selo-recorrencia">
      <app-icone nome="repeat" rotulo="Recorrência" />
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
      color: var(--muted-foreground);
      font: var(--fonte-rotulo-p);
    }
    app-icone {
      width: 16px;
      height: 16px;
      font-size: 16px;
      line-height: 16px;
    }
    .alterada {
      padding: 0 6px;
      border-radius: 4px;
      background: var(--accent);
      color: var(--accent-foreground);
    }
  `,
})
export class SeloRecorrencia {
  readonly recorrenciaId = input<string | null | undefined>();
  readonly alterada = input<boolean | null | undefined>();
}
