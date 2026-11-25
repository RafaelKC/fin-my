import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Canal que criou o lançamento (`createdVia` das respostas REST; 008 FR-008). */
export type CriadoVia = 'app' | 'claude' | 'system';

/** Selo "via Claude": aparece só para lançamentos criados pelo assistente (008 research R5). */
@Component({
  selector: 'app-selo-via-claude',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (via() === 'claude') {
    <span class="selo-claude" data-testid="selo-via-claude">via Claude</span>
  }`,
  styles: `
    .selo-claude {
      margin-left: 8px;
      padding: 0 6px;
      border-radius: 4px;
      background: var(--previsto-suave);
      color: var(--previsto);
      font: var(--fonte-rotulo-p);
      white-space: nowrap;
    }
  `,
})
export class SeloViaClaude {
  readonly via = input<CriadoVia | null | undefined>();
}
