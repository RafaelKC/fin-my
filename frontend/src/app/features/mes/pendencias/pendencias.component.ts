import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { problemMessage } from '../../../core/errors/problem-details';
import { BrlPipe } from '../../../core/money';
import { Wallet } from '../../carteiras/wallet.models';
import { Entry } from '../../lancamento/entry.models';
import { formatarData } from '../mes-logic';
import { MonthView } from '../month.models';
import { AcaoPendencia, LOTE_MAXIMO } from './pendencias.models';
import { mensagemExclusao, rotuloRealizar, tituloAviso } from './pendencias.logic';
import { PendenciasService } from './pendencias.service';
import { Avisos } from '@/shared/ui/avisos';
import { ZardButtonComponent } from '@/shared/components/button';

/** Tempo do "Desfazer" depois de excluir (RT-09). */
export const DESFAZER_MS = 8000;

/**
 * Aviso e lista de pendências (US4; FR-019/FR-020): aparece só no mês de hoje, com a contagem e os
 * totais a receber e a pagar; abre a lista com seleção múltipla e as ações Marcar como
 * recebida/paga, Trazer para este mês e Excluir (com Desfazer).
 */
@Component({
  selector: 'app-pendencias',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ZardButtonComponent, BrlPipe],
  templateUrl: './pendencias.component.html',
  styleUrl: './pendencias.component.scss',
})
export class PendenciasComponent {
  private readonly service = inject(PendenciasService);
  private readonly avisos = inject(Avisos);

  readonly resumo = input.required<MonthView['pending']>();
  readonly carteiras = input<Wallet[]>([]);

  protected readonly aberta = signal(false);
  protected readonly itens = signal<Entry[]>([]);
  protected readonly selecionados = signal<ReadonlySet<string>>(new Set());
  protected readonly carregando = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly titulo = computed(() => tituloAviso(this.resumo().count));
  protected readonly selecionadas = computed(() =>
    this.itens().filter((e) => this.selecionados().has(e.id)),
  );
  protected readonly rotuloRealizar = computed(() => rotuloRealizar(this.selecionadas()));
  protected readonly todasSelecionadas = computed(
    () => this.itens().length > 0 && this.selecionadas().length === this.itens().length,
  );

  constructor() {
    // A lista aberta acompanha o aviso: qualquer recarga da visão do mês a atualiza.
    effect(() => {
      this.resumo();
      if (untracked(this.aberta)) untracked(() => this.carregar());
    });
  }

  protected alternar(): void {
    const abrir = !this.aberta();
    this.aberta.set(abrir);
    if (abrir) this.carregar();
  }

  protected alternarItem(id: string): void {
    this.selecionados.update((atual) => {
      const proximo = new Set(atual);
      if (!proximo.delete(id)) proximo.add(id);
      return proximo;
    });
  }

  protected alternarTodas(): void {
    this.selecionados.set(
      this.todasSelecionadas()
        ? new Set()
        : new Set(
            this.itens()
              .slice(0, LOTE_MAXIMO)
              .map((e) => e.id),
          ),
    );
  }

  protected realizar(): void {
    this.aplicar('settle');
  }

  protected trazer(): void {
    this.aplicar('move_to_current_month');
  }

  protected excluir(): void {
    this.aplicar('delete');
  }

  protected data(data: string): string {
    return formatarData(data);
  }

  protected nomeCarteira(id: string): string {
    return this.carteiras().find((w) => w.id === id)?.name ?? '';
  }

  protected rotuloTipo(entry: Entry): string {
    return entry.kind === 'income' ? 'A receber' : 'A pagar';
  }

  private aplicar(acao: AcaoPendencia): void {
    const ids = this.selecionadas().map((e) => e.id);
    if (ids.length === 0 || this.ocupado()) return;
    this.ocupado.set(true);
    this.erro.set(null);
    this.service.bulk(ids, acao).subscribe({
      next: (resultado) => {
        this.ocupado.set(false);
        this.selecionados.set(new Set());
        if (acao === 'delete' && resultado.deletionBatchId !== null) {
          this.oferecerDesfazer(resultado.deletionBatchId, resultado.affected);
        }
        // A visão do mês recarrega (alteradas$) e traz o novo `resumo`, que atualiza a lista.
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        this.erro.set(problemMessage(error));
        this.carregar(); // o lote é tudo ou nada: a lista volta ao que o servidor tem
      },
    });
  }

  private oferecerDesfazer(batchId: string, afetadas: number): void {
    const ref = this.avisos.open(mensagemExclusao(afetadas), 'Desfazer', {
      duration: DESFAZER_MS,
      verticalPosition: 'top',
    });
    ref.onAction().subscribe(() => {
      this.service.restaurar(batchId).subscribe({
        next: () =>
          this.avisos.open('Exclusão desfeita.', undefined, {
            duration: 4000,
            verticalPosition: 'top',
          }),
        error: (error: unknown) =>
          this.avisos.open(problemMessage(error), 'Fechar', { verticalPosition: 'top' }),
      });
    });
  }

  private carregar(): void {
    this.carregando.set(true);
    this.service.list().subscribe({
      next: (lista) => {
        this.carregando.set(false);
        this.itens.set(lista);
        const ids = new Set(lista.map((e) => e.id));
        this.selecionados.update((atual) => new Set([...atual].filter((id) => ids.has(id))));
      },
      error: (error: unknown) => {
        this.carregando.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }
}
