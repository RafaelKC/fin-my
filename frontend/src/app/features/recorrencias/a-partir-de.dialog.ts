import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { isYearMonth } from '../../core/money';
import { DADOS_DIALOGO, DialogoRef } from '@/shared/ui/dialogo';
import { ZardButtonComponent } from '@/shared/components/button';
import { CampoDirective } from '@/shared/ui/campo.directive';
import { ZardDialogFooterComponent, ZardDialogTitleComponent } from '@/shared/components/dialog';

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
  imports: [
    ZardButtonComponent,
    CampoDirective,
    ZardDialogFooterComponent,
    ZardDialogTitleComponent,
  ],
  template: `
    <h2 z-dialog-title>{{ data.titulo }}</h2>
    <div class="dialogo-corpo">
      @if (data.mensagem) {
        <p>{{ data.mensagem }}</p>
      }
      <label for="a-partir-de-mes">A partir de qual mês?</label>
      <input
        appCampo
        id="a-partir-de-mes"
        type="month"
        class="mes"
        data-testid="a-partir-de-mes"
        [value]="mes()"
        (input)="mes.set($any($event.target).value)"
      />
    </div>
    <footer z-dialog-footer>
      <button
        z-button
        zType="outline"
        type="button"
        class="botao"
        data-testid="cancelar"
        (click)="ref.close(null)"
      >
        Cancelar
      </button>
      <button
        z-button
        type="button"
        class="botao principal"
        data-testid="confirmar"
        [disabled]="!valido()"
        (click)="ref.close(mes())"
      >
        {{ data.confirmar }}
      </button>
    </footer>
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
  `,
})
export class APartirDeDialog {
  protected readonly data = inject<APartirDeData>(DADOS_DIALOGO);
  protected readonly ref = inject<DialogoRef<APartirDeDialog, APartirDeResultado>>(DialogoRef);
  protected readonly mes = signal(this.data.mesPadrao);
  protected readonly valido = computed(() => isYearMonth(this.mes()));
}
