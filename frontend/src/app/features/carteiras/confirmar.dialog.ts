import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Wallet } from './wallet.models';
import { DADOS_DIALOGO, DialogoRef } from '@/shared/ui/dialogo';
import { ZardButtonComponent } from '@/shared/components/button';
import { ZardDialogFooterComponent, ZardDialogTitleComponent } from '@/shared/components/dialog';
import { ZardNativeSelectDirective } from '@/shared/components/native-select';

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
  imports: [
    ZardButtonComponent,
    ZardDialogFooterComponent,
    ZardDialogTitleComponent,
    ZardNativeSelectDirective,
  ],
  template: `
    <h2 z-dialog-title>{{ data.titulo }}</h2>
    <div class="dialogo-corpo">
      <p>{{ data.mensagem }}</p>
      @if (data.escolherPrincipal; as opcoes) {
        <label for="nova-principal">Nova carteira principal</label>
        <select
          z-native-select
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
    </div>
    <footer z-dialog-footer>
      <button z-button zType="outline" type="button" class="botao" (click)="ref.close(null)">
        Cancelar
      </button>
      <button
        z-button
        type="button"
        class="botao principal"
        data-testid="confirmar"
        [disabled]="data.escolherPrincipal && !escolhida()"
        (click)="ref.close(escolhida())"
      >
        {{ data.confirmar }}
      </button>
    </footer>
  `,
  styles: `
    .escolha {
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
export class ConfirmarDialog {
  protected readonly data = inject<ConfirmarData>(DADOS_DIALOGO);
  protected readonly ref = inject<DialogoRef<ConfirmarDialog, ConfirmarResultado>>(DialogoRef);
  protected readonly escolhida = signal('');
}
