import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DESCRICAO_MAX } from '../lancamento/entry.models';
import { PlanningKind } from './planning.models';
import { DADOS_DIALOGO, DialogoRef } from '@/shared/ui/dialogo';
import { ZardButtonComponent } from '@/shared/components/button';
import { CampoDirective } from '@/shared/ui/campo.directive';
import { ZardDialogFooterComponent, ZardDialogTitleComponent } from '@/shared/components/dialog';
import { ZardNativeSelectDirective } from '@/shared/components/native-select';

export interface OpcaoCarteira {
  id: string;
  name: string;
}

export interface NovaLinhaData {
  carteiras: OpcaoCarteira[];
  /** Carteira principal, escolhida de início. */
  carteiraPadrao: string;
}

export interface NovaLinhaResultado {
  kind: PlanningKind;
  description: string;
  walletId: string;
  day: number;
}

/** Diálogo "Nova linha" (US1.8): tipo, descrição, carteira e dia; a linha só vive na tela até receber valores. */
@Component({
  selector: 'app-nova-linha-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ZardButtonComponent,
    CampoDirective,
    ZardDialogFooterComponent,
    ZardDialogTitleComponent,
    ZardNativeSelectDirective,
  ],
  styleUrl: './dialogos.scss',
  template: `
    <h2 z-dialog-title>Nova linha</h2>
    <div class="dialogo-corpo">
      <div class="campo">
        <span id="nl-tipo">Tipo</span>
        <div class="opcoes" role="group" aria-labelledby="nl-tipo">
          <button
            type="button"
            class="chip"
            data-testid="nova-linha-saida"
            [attr.aria-pressed]="kind() === 'expense'"
            (click)="kind.set('expense')"
          >
            Saída
          </button>
          <button
            type="button"
            class="chip"
            data-testid="nova-linha-entrada"
            [attr.aria-pressed]="kind() === 'income'"
            (click)="kind.set('income')"
          >
            Entrada
          </button>
        </div>
      </div>
      <div class="campo">
        <label for="nl-descricao">Descrição</label>
        <input
          appCampo
          id="nl-descricao"
          type="text"
          data-testid="nova-linha-descricao"
          autocomplete="off"
          [maxLength]="descricaoMax"
          [value]="descricao()"
          (input)="descricao.set($any($event.target).value)"
        />
      </div>
      <div class="campo">
        <label for="nl-carteira">Carteira</label>
        <select
          z-native-select
          id="nl-carteira"
          data-testid="nova-linha-carteira"
          (change)="carteira.set($any($event.target).value)"
        >
          @for (c of data.carteiras; track c.id) {
            <option [value]="c.id" [selected]="c.id === carteira()">{{ c.name }}</option>
          }
        </select>
      </div>
      <div class="campo">
        <label for="nl-dia">Dia do mês</label>
        <input
          appCampo
          id="nl-dia"
          type="number"
          min="1"
          max="31"
          inputmode="numeric"
          data-testid="nova-linha-dia"
          [value]="dia()"
          (input)="dia.set($any($event.target).value)"
        />
        @if (!diaValido()) {
          <span class="erro">Informe um dia de 1 a 31.</span>
        }
      </div>
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
        data-testid="nova-linha-criar"
        [disabled]="!valido()"
        (click)="criar()"
      >
        Criar linha
      </button>
    </footer>
  `,
})
export class NovaLinhaDialog {
  protected readonly data = inject<NovaLinhaData>(DADOS_DIALOGO);
  protected readonly ref =
    inject<DialogoRef<NovaLinhaDialog, NovaLinhaResultado | null>>(DialogoRef);
  protected readonly descricaoMax = DESCRICAO_MAX;

  protected readonly kind = signal<PlanningKind>('expense');
  protected readonly descricao = signal('');
  protected readonly carteira = signal(this.data.carteiraPadrao);
  protected readonly dia = signal('1');

  protected readonly diaValido = computed(() => {
    const n = Number(this.dia());
    return Number.isInteger(n) && n >= 1 && n <= 31;
  });
  protected readonly valido = computed(
    () => this.descricao().trim() !== '' && this.diaValido() && this.carteira() !== '',
  );

  protected criar(): void {
    if (!this.valido()) return;
    this.ref.close({
      kind: this.kind(),
      description: this.descricao().trim(),
      walletId: this.carteira(),
      day: Number(this.dia()),
    });
  }
}
