import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DADOS_DIALOGO, DialogoRef } from '@/shared/ui/dialogo';
import { ZardButtonComponent } from '@/shared/components/button';
import { ZardDialogFooterComponent, ZardDialogTitleComponent } from '@/shared/components/dialog';

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
  imports: [ZardButtonComponent, ZardDialogFooterComponent, ZardDialogTitleComponent],
  template: `
    <h2 z-dialog-title>{{ data.titulo }}</h2>
    @if (data.mensagem) {
      <div class="dialogo-corpo">
        <p>{{ data.mensagem }}</p>
      </div>
    }
    <footer z-dialog-footer>
      @for (o of data.opcoes; track o.valor) {
        <button
          z-button
          zType="outline"
          type="button"
          class="botao"
          [attr.data-testid]="'escopo-' + o.valor"
          (click)="ref.close(o.valor)"
        >
          {{ o.rotulo }}
        </button>
      }
      <button z-button zType="outline" type="button" class="botao" (click)="ref.close(null)">
        Cancelar
      </button>
    </footer>
  `,
  styles: `
    .acoes {
      flex-direction: column;
      align-items: stretch;
    }
  `,
})
export class EscopoDialog<T extends string = string> {
  protected readonly data = inject<EscopoData<T>>(DADOS_DIALOGO);
  protected readonly ref = inject<DialogoRef<EscopoDialog<T>, T | null>>(DialogoRef);
}
