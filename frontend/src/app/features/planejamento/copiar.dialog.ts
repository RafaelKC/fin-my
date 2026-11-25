import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { problemMessage } from '../../core/errors/problem-details';
import { formatBrl, monthShortLabel } from '../../core/money';
import { CopyCandidateRow, CopyCandidates, PlanningCopyResult } from './planning.models';
import { PlanningService } from './planning.service';
import { DADOS_DIALOGO, DialogoRef } from '@/shared/ui/dialogo';
import { ZardButtonComponent } from '@/shared/components/button';
import { ZardDialogFooterComponent, ZardDialogTitleComponent } from '@/shared/components/dialog';

export interface CopiarData {
  /** Ano de destino; a origem é o ano anterior. */
  ano: number;
}

/** Resultado: `null` se cancelou; senão o que o servidor criou e pulou. */
export type CopiarResultado = PlanningCopyResult | null;

/**
 * Diálogo "Copiar do ano anterior" (007 US3, FR-013, research R4): lista as linhas do ano anterior
 * (sem ocorrências de recorrência; partes de divisão separadas por tag de acerto) com os valores
 * por mês, e só cria algo depois da confirmação.
 */
@Component({
  selector: 'app-copiar-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ZardButtonComponent, ZardDialogFooterComponent, ZardDialogTitleComponent],
  styleUrl: './dialogos.scss',
  template: `
    <h2 z-dialog-title>Copiar de {{ data.ano - 1 }} para {{ data.ano }}</h2>
    <div class="dialogo-corpo">
      @if (carregando()) {
        <p role="status">Carregando…</p>
      } @else if (candidatos(); as c) {
        @if (c.rows.length === 0 && c.estimates.length === 0) {
          <p data-testid="copiar-vazio">Não há nada para copiar de {{ data.ano - 1 }}.</p>
        }
        @if (c.rows.length > 0) {
          <ul class="copiar-lista" data-testid="copiar-linhas">
            @for (l of c.rows; track l.key) {
              <li data-testid="copiar-linha" [attr.data-chave]="l.key">
                <label>
                  <input
                    type="checkbox"
                    data-testid="copiar-marcar"
                    [checked]="marcadas().has(l.key)"
                    (change)="alternar(l.key)"
                  />
                  <span class="copiar-nome">
                    {{ l.kind === 'income' ? 'Entrada' : 'Saída' }} · {{ l.description }}
                    @if (l.settlementTagName) {
                      <span class="copiar-tag">({{ l.settlementTagName }})</span>
                    }
                  </span>
                </label>
                <span class="copiar-valores" data-testid="copiar-valores">{{ resumo(l) }}</span>
              </li>
            }
          </ul>
          <p class="nota">Meses que já têm valor em {{ data.ano }} são pulados.</p>
        }
        @if (c.estimates.length > 0) {
          <label class="copiar-estimativas">
            <input
              type="checkbox"
              data-testid="copiar-estimativas"
              [checked]="estimativas()"
              (change)="estimativas.set(!estimativas())"
            />
            Copiar estimativas de cartão ({{ nomesDosCartoes() }})
          </label>
        }
      }
      @if (erro(); as mensagem) {
        <p class="erro" role="alert" data-testid="copiar-erro">{{ mensagem }}</p>
      }
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
        data-testid="copiar-confirmar"
        [disabled]="!podeConfirmar()"
        (click)="confirmar()"
      >
        Copiar
      </button>
    </footer>
  `,
})
export class CopiarDialog {
  protected readonly data = inject<CopiarData>(DADOS_DIALOGO);
  protected readonly ref = inject<DialogoRef<CopiarDialog, CopiarResultado>>(DialogoRef);
  private readonly service = inject(PlanningService);
  /** Uma chave por diálogo: reenviar o mesmo pedido não copia duas vezes. */
  private readonly chave = crypto.randomUUID();

  protected readonly carregando = signal(true);
  protected readonly candidatos = signal<CopyCandidates | null>(null);
  protected readonly marcadas = signal<ReadonlySet<string>>(new Set());
  protected readonly estimativas = signal(false);
  protected readonly enviando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly nomesDosCartoes = computed(
    () =>
      this.candidatos()
        ?.estimates.map((e) => e.creditCardName)
        .join(', ') ?? '',
  );
  protected readonly podeConfirmar = computed(
    () => !this.enviando() && (this.marcadas().size > 0 || this.estimativas()),
  );

  constructor() {
    this.service.copyCandidates(this.data.ano).subscribe({
      next: (c) => {
        this.candidatos.set(c);
        this.carregando.set(false);
      },
      error: (error: unknown) => {
        this.carregando.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }

  protected alternar(chave: string): void {
    const novo = new Set(this.marcadas());
    if (!novo.delete(chave)) novo.add(chave);
    this.marcadas.set(novo);
  }

  /** "dez R$ 1.200,00 · jan R$ 50,00": só os meses com valor. */
  protected resumo(linha: CopyCandidateRow): string {
    return linha.cells
      .filter((c) => c.itemCount > 0)
      .map((c) => `${monthShortLabel(c.month).slice(0, 3)} ${formatBrl(c.plannedCents)}`)
      .join(' · ');
  }

  protected confirmar(): void {
    if (!this.podeConfirmar()) return;
    this.enviando.set(true);
    this.erro.set(null);
    this.service
      .copy(
        this.data.ano,
        { rowKeys: [...this.marcadas()], copyEstimates: this.estimativas() },
        this.chave,
      )
      .subscribe({
        next: (resultado) => this.ref.close(resultado),
        error: (error: unknown) => {
          this.enviando.set(false);
          this.erro.set(problemMessage(error));
        },
      });
  }
}
