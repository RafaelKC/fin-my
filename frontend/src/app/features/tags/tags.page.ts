import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { Icone } from '@/shared/ui/icone';
import { asProblem, problemMessage } from '../../core/errors/problem-details';
import { TAG_NOME_MAX, Tag, TagConflito } from '../../shared/tag-picker/tag.models';
import { FiltroTagsArquivadas, TagsService } from '../../shared/tag-picker/tags.service';
import { ZardButtonComponent } from '@/shared/components/button';
import { CampoDirective } from '@/shared/ui/campo.directive';
import { ZardNativeSelectDirective } from '@/shared/components/native-select';

/** Ação em andamento sobre uma tag: edição, junção ou confirmação de exclusão. */
export type PainelTag =
  | { tipo: 'editar'; tag: Tag; nome: string; cor: string; deAcerto: boolean }
  | { tipo: 'juntar'; tag: Tag; destinoId: string; confirmando: boolean }
  | { tipo: 'excluir'; tag: Tag };

/** Tela `/tags` (US5, contracts/ui-rotas.md): editar, arquivar, excluir e juntar tags. */
@Component({
  selector: 'app-tags-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ZardButtonComponent, CampoDirective, ZardNativeSelectDirective, FormsModule, Icone],
  templateUrl: './tags.page.html',
  styleUrl: './tags.page.scss',
})
export class TagsPage {
  private readonly tagsService = inject(TagsService);

  protected readonly nomeMax = TAG_NOME_MAX;
  protected readonly tags = signal<Tag[] | null>(null);
  protected readonly filtro = signal<FiltroTagsArquivadas>('false');
  protected readonly erroLista = signal<string | null>(null);
  protected readonly painel = signal<PainelTag | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly conflitos = signal<TagConflito[]>([]);

  constructor() {
    this.carregar();
  }

  protected mostrarArquivadas(arquivadas: boolean): void {
    this.filtro.set(arquivadas ? 'true' : 'false');
    this.fechar();
    this.carregar();
  }

  protected carregar(): void {
    this.erroLista.set(null);
    this.tagsService.list(this.filtro()).subscribe({
      next: (lista) => this.tags.set(lista),
      error: (e: unknown) => {
        this.tags.set([]);
        this.erroLista.set(problemMessage(e));
      },
    });
  }

  protected abrirEdicao(tag: Tag): void {
    this.abrir({ tipo: 'editar', tag, nome: tag.name, cor: tag.color, deAcerto: tag.isSettlement });
  }

  protected abrirJuncao(tag: Tag): void {
    this.abrir({ tipo: 'juntar', tag, destinoId: '', confirmando: false });
  }

  protected abrirExclusao(tag: Tag): void {
    this.abrir({ tipo: 'excluir', tag });
  }

  protected fechar(): void {
    this.painel.set(null);
    this.erro.set(null);
    this.conflitos.set([]);
  }

  /** Possíveis destinos da junção: as demais tags ativas. */
  protected destinos(tag: Tag): Tag[] {
    return (this.tags() ?? []).filter((t) => t.id !== tag.id && !t.archived);
  }

  protected nomeDoDestino(id: string): string {
    return this.tags()?.find((t) => t.id === id)?.name ?? '';
  }

  protected atualizarEdicao(parcial: Partial<{ nome: string; cor: string; deAcerto: boolean }>) {
    const p = this.painel();
    if (p?.tipo !== 'editar') return;
    this.painel.set({ ...p, ...parcial });
  }

  protected escolherDestino(destinoId: string): void {
    const p = this.painel();
    if (p?.tipo !== 'juntar') return;
    this.painel.set({ ...p, destinoId, confirmando: false });
  }

  protected pedirConfirmacaoJuncao(): void {
    const p = this.painel();
    if (p?.tipo !== 'juntar' || p.destinoId === '') return;
    this.painel.set({ ...p, confirmando: true });
  }

  protected salvarEdicao(): void {
    const p = this.painel();
    if (p?.tipo !== 'editar') return;
    const nome = p.nome.trim();
    if (nome === '' || nome.length > TAG_NOME_MAX) {
      this.erro.set(`Informe um nome de até ${TAG_NOME_MAX} caracteres.`);
      return;
    }
    this.executar(
      this.tagsService.update(p.tag.id, {
        name: nome,
        color: p.cor,
        isSettlement: p.deAcerto,
        version: p.tag.version,
      }),
    );
  }

  protected confirmarJuncao(): void {
    const p = this.painel();
    if (p?.tipo !== 'juntar' || p.destinoId === '') return;
    this.executar(this.tagsService.merge(p.tag.id, p.destinoId));
  }

  protected confirmarExclusao(): void {
    const p = this.painel();
    if (p?.tipo !== 'excluir') return;
    this.executar(this.tagsService.remove(p.tag.id));
  }

  protected arquivar(tag: Tag): void {
    this.abrir(null);
    this.executar(
      tag.archived ? this.tagsService.unarchive(tag.id) : this.tagsService.archive(tag.id),
    );
  }

  private abrir(painel: PainelTag | null): void {
    this.erro.set(null);
    this.conflitos.set([]);
    this.painel.set(painel);
  }

  private executar(chamada: Observable<unknown>): void {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    this.erro.set(null);
    this.conflitos.set([]);
    chamada.subscribe({
      next: () => {
        this.ocupado.set(false);
        this.painel.set(null);
        this.carregar();
      },
      error: (e: unknown) => {
        this.ocupado.set(false);
        this.erro.set(problemMessage(e));
        this.conflitos.set(asProblem(e)?.conflicts ?? []);
        if (asProblem(e)?.code === 'concurrency_conflict') this.carregar();
      },
    });
  }
}
