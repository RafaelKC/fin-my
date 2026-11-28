import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { BancoLogo } from './banco-logo';
import { BANCOS, Banco } from './bancos';

/** Escolha do banco da carteira/cartão: grade de logos com "Nenhum". Escolher um banco também sugere nome e cor. */
@Component({
  selector: 'app-banco-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BancoLogo],
  template: `
    <fieldset class="bancos">
      <legend>Banco</legend>
      <div class="grade" role="radiogroup" aria-label="Banco">
        <button
          type="button"
          role="radio"
          class="banco nenhum"
          [attr.aria-checked]="!valor()"
          [class.escolhido]="!valor()"
          (click)="escolher.emit(null)"
        >
          Nenhum
        </button>
        @for (b of bancos; track b.codigo) {
          <button
            type="button"
            role="radio"
            class="banco"
            [attr.aria-checked]="valor() === b.codigo"
            [class.escolhido]="valor() === b.codigo"
            [attr.data-banco]="b.codigo"
            (click)="escolher.emit(b)"
          >
            <app-banco-logo [codigo]="b.codigo" [tamanho]="26" />
            <span>{{ b.nome }}</span>
          </button>
        }
      </div>
    </fieldset>
  `,
  styles: `
    .bancos {
      border: 0;
      margin: 0;
      padding: 0;
      min-width: 0;
    }
    legend {
      font: var(--fonte-rotulo-g, inherit);
      margin-bottom: 8px;
      padding: 0;
    }
    .grade {
      display: flex;
      gap: 8px;
      overflow-x: auto;
      padding: 2px 2px 6px;
      scrollbar-width: thin;
    }
    .banco {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      flex: none;
      min-height: 44px;
      padding: 6px 12px 6px 6px;
      border-radius: 999px;
      border: 1px solid var(--border);
      background: var(--card);
      color: var(--foreground);
      font: inherit;
      font-size: 0.875rem;
      cursor: pointer;
      white-space: nowrap;
    }
    .banco.nenhum {
      padding: 6px 14px;
    }
    .banco.escolhido {
      border-color: var(--primary);
      box-shadow: 0 0 0 2px var(--primary);
    }
    .banco:focus-visible {
      outline: 2px solid var(--ring, var(--primary));
      outline-offset: 2px;
    }
  `,
})
export class BancoPicker {
  readonly valor = input<string | null>(null);
  readonly escolher = output<Banco | null>();
  protected readonly bancos = BANCOS;
}
