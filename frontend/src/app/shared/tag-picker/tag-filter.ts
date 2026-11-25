import { ChangeDetectionStrategy, Component, computed, inject, model, signal } from '@angular/core';
import { Icone } from '@/shared/ui/icone';
import { problemMessage } from '../../core/errors/problem-details';
import { ordenarPorUso } from './tag-picker-logic';
import { Tag } from './tag.models';
import { TagsService } from './tags.service';

/**
 * Filtro "Tags" da visão do mês, da fatura e da busca (004 FR-004): vários ao mesmo tempo, com
 * "qualquer uma delas". A lista de tags só é pedida quando o painel é aberto pela primeira vez.
 */
@Component({
  selector: 'app-tag-filter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icone],
  templateUrl: './tag-filter.html',
  styleUrl: './tag-filter.scss',
})
export class TagFilter {
  private readonly api = inject(TagsService);

  /** Ids filtrados; vazio = sem filtro. */
  readonly ids = model<string[]>([]);

  protected readonly aberto = signal(false);
  protected readonly carregando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly tags = signal<Tag[]>([]);
  private pedido = false;

  protected readonly ordenadas = computed(() => ordenarPorUso(this.tags()));
  protected readonly rotulo = computed(() =>
    this.ids().length === 0 ? 'Tags' : `Tags (${this.ids().length})`,
  );

  protected alternarPainel(): void {
    this.aberto.update((v) => !v);
    if (this.aberto()) this.carregar();
  }

  protected alternar(tag: Tag): void {
    this.ids.update((l) => (l.includes(tag.id) ? l.filter((id) => id !== tag.id) : [...l, tag.id]));
  }

  protected limpar(): void {
    this.ids.set([]);
  }

  protected escolhida(tag: Tag): boolean {
    return this.ids().includes(tag.id);
  }

  private carregar(): void {
    if (this.pedido) return;
    this.pedido = true;
    this.carregando.set(true);
    this.api.list('all').subscribe({
      next: (lista) => {
        this.tags.set(lista);
        this.carregando.set(false);
      },
      error: (error: unknown) => {
        this.pedido = false;
        this.carregando.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }
}
