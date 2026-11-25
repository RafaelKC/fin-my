import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { parseValueExpression, valueExpressionMessages } from '../../core/money';
import { AmountInput } from '../../shared/amount-input/amount-input';
import { DADOS_DIALOGO, DialogoRef } from '@/shared/ui/dialogo';
import { ZardButtonComponent } from '@/shared/components/button';
import { ZardDialogFooterComponent, ZardDialogTitleComponent } from '@/shared/components/dialog';

export interface CelulaData {
  titulo: string;
  /** Valor atual no formato do campo (`1.200,00` sem o ponto de milhar → `1200,00`); vazio se não há. */
  valorInicial: string;
}

/** Resultado: `null` se cancelou; senão o texto do valor (`''` apaga a célula). */
export type CelulaResultado = string | null;

/** Edição de célula por diálogo (celular, research R5): o campo com calculadora e "Limpar". */
@Component({
  selector: 'app-celula-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ZardButtonComponent,
    ZardDialogFooterComponent,
    ZardDialogTitleComponent,
    ReactiveFormsModule,
    AmountInput,
  ],
  styleUrl: './dialogos.scss',
  template: `
    <h2 z-dialog-title>{{ data.titulo }}</h2>
    <div class="dialogo-corpo">
      <app-amount-input [formControl]="valor" />
      @if (erro(); as mensagem) {
        <p class="erro" role="alert">{{ mensagem }}</p>
      }
    </div>
    <footer z-dialog-footer>
      @if (data.valorInicial !== '') {
        <button
          z-button
          zType="outline"
          type="button"
          class="botao"
          data-testid="celula-limpar"
          (click)="ref.close('')"
        >
          Limpar
        </button>
      }
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
        data-testid="celula-salvar"
        (click)="salvar()"
      >
        Salvar
      </button>
    </footer>
  `,
})
export class CelulaDialog {
  protected readonly data = inject<CelulaData>(DADOS_DIALOGO);
  protected readonly ref = inject<DialogoRef<CelulaDialog, CelulaResultado>>(DialogoRef);
  protected readonly valor = new FormControl(this.data.valorInicial, { nonNullable: true });
  protected readonly erro = signal<string | null>(null);

  protected salvar(): void {
    const texto = this.valor.value.trim();
    if (texto === '') {
      this.ref.close('');
      return;
    }
    const analise = parseValueExpression(texto, false);
    if (!analise.ok) {
      this.erro.set(valueExpressionMessages[analise.error]);
      return;
    }
    this.ref.close(texto);
  }
}
