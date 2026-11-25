import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Icone } from '@/shared/ui/icone';
import { asProblem, problemMessage } from '../../core/errors/problem-details';
import { buscarTags, chipsFrequentes, tagComNome } from './tag-picker-logic';
import { MENSAGEM_DUAS_DE_ACERTO, TAGS_MAX, TAG_NOME_MAX, Tag } from './tag.models';
import { TagsService } from './tags.service';
import { CampoDirective } from '@/shared/ui/campo.directive';

/**
 * Campo Tags do formulário rápido (004 US1, FR-003; contracts/ui-rotas.md): as 8 tags mais usadas
 * como chips de um toque, busca que filtra as demais e "Criar tag" para um nome novo (não de acerto).
 * Tags de acerto levam o ícone de pessoa; uma segunda tag de acerto é recusada com o aviso de
 * "Dividir". Tags arquivadas já no lançamento continuam visíveis e removíveis, mas não voltam a ser
 * adicionadas (research R1).
 */
@Component({
  selector: 'app-tag-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CampoDirective, Icone],
  templateUrl: './tag-picker.html',
  styleUrl: './tag-picker.scss',
})
export class TagPicker {
  private readonly api = inject(TagsService);

  /** Ids escolhidos (tags de acerto ou não), em ordem de escolha. */
  readonly ids = model<string[]>([]);
  /** `true` só quando o formulário sabe dividir (T009): mostra o atalho "Dividir" no aviso. */
  readonly dividirDisponivel = input(false);
  /** Emitido só por escolhas da pessoa (não por carga ou sugestão). */
  readonly alterado = output<void>();
  readonly dividir = output<void>();

  protected readonly tags = signal<Tag[]>([]);
  protected readonly carregando = signal(true);
  protected readonly erroCarga = signal<string | null>(null);
  protected readonly texto = signal('');
  protected readonly erro = signal<string | null>(null);
  protected readonly avisoAcerto = signal(false);
  protected readonly criando = signal(false);
  protected readonly nomeMax = TAG_NOME_MAX;

  private readonly porId = computed(() => new Map(this.tags().map((t) => [t.id, t])));

  /** Chips de um toque: as 8 mais usadas e, depois, o que já foi escolhido fora delas. */
  protected readonly chips = computed<Tag[]>(() => {
    const frequentes = chipsFrequentes(this.tags());
    const extras = this.ids()
      .map((id) => this.porId().get(id))
      .filter((t): t is Tag => t !== undefined && !frequentes.some((f) => f.id === t.id));
    return [...frequentes, ...extras];
  });
  protected readonly resultados = computed(() =>
    buscarTags(this.tags(), this.texto(), this.ids()).filter(
      (t) => !this.chips().some((c) => c.id === t.id),
    ),
  );
  /** Nome novo: texto digitado que não é (normalizado) o de nenhuma tag existente. */
  protected readonly nomeNovo = computed(() => {
    const t = this.texto().trim();
    return t === '' || tagComNome(this.tags(), t) !== null ? null : t;
  });

  constructor() {
    this.carregar();
  }

  protected escolhida(tag: Tag): boolean {
    return this.ids().includes(tag.id);
  }

  protected alternar(tag: Tag): void {
    this.erro.set(null);
    if (this.escolhida(tag)) {
      this.avisoAcerto.set(false);
      this.ids.update((l) => l.filter((id) => id !== tag.id));
      this.alterado.emit();
      return;
    }
    this.adicionar(tag);
  }

  protected aoDigitar(valor: string): void {
    this.texto.set(valor);
    this.erro.set(null);
  }

  /** Enter: usa a tag de mesmo nome (US1.3) ou cria uma nova (US1.2). */
  protected confirmarTexto(evento?: Event): void {
    evento?.preventDefault();
    const texto = this.texto().trim();
    if (texto === '') return;
    const existente = tagComNome(this.tags(), texto);
    if (existente !== null) {
      if (!this.escolhida(existente)) this.adicionar(existente);
      this.texto.set('');
      return;
    }
    this.criar(texto);
  }

  protected criar(nome: string): void {
    if (this.criando()) return;
    if (nome.length > TAG_NOME_MAX) {
      this.erro.set(`O nome da tag pode ter até ${TAG_NOME_MAX} caracteres.`);
      return;
    }
    if (this.ids().length >= TAGS_MAX) {
      this.erro.set(`No máximo ${TAGS_MAX} tags por lançamento.`);
      return;
    }
    this.criando.set(true);
    this.erro.set(null);
    this.api.create({ name: nome }).subscribe({
      next: (tag) => {
        this.criando.set(false);
        this.tags.update((l) => [...l, tag]);
        this.texto.set('');
        this.adicionar(tag);
      },
      error: (error: unknown) => {
        this.criando.set(false);
        const problem = asProblem(error);
        const existente = problem?.existingTagId;
        if (problem?.code === 'tag.name_taken' && existente) {
          this.usarExistente(existente);
          return;
        }
        this.erro.set(problemMessage(error));
      },
    });
  }

  protected irParaDividir(): void {
    this.avisoAcerto.set(false);
    this.dividir.emit();
  }

  /** Corrida entre aparelhos: o nome já existe; usa a existente (recarregando se ainda não a temos). */
  private usarExistente(id: string): void {
    const pronta = (tag: Tag | undefined) => {
      if (tag === undefined) {
        this.erro.set('Não foi possível usar a tag existente. Tente de novo.');
        return;
      }
      this.texto.set('');
      this.adicionar(tag);
    };
    const local = this.porId().get(id);
    if (local !== undefined) {
      pronta(local);
      return;
    }
    this.api.list('all').subscribe({
      next: (lista) => {
        this.tags.set(lista);
        pronta(lista.find((t) => t.id === id));
      },
      error: (error: unknown) => this.erro.set(problemMessage(error)),
    });
  }

  private adicionar(tag: Tag): void {
    if (tag.archived) {
      this.erro.set(`A tag "${tag.name}" está arquivada.`);
      return;
    }
    if (this.ids().length >= TAGS_MAX) {
      this.erro.set(`No máximo ${TAGS_MAX} tags por lançamento.`);
      return;
    }
    if (tag.isSettlement && this.ids().some((id) => this.porId().get(id)?.isSettlement)) {
      // US1.4: não adiciona; o aviso sugere Dividir.
      this.avisoAcerto.set(true);
      return;
    }
    this.avisoAcerto.set(false);
    this.ids.update((l) => [...l, tag.id]);
    this.alterado.emit();
  }

  private carregar(): void {
    // Com arquivadas: um lançamento pode ter uma tag arquivada, que continua escolhida.
    this.api
      .list('all')
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (lista) => {
          this.tags.set(lista);
          this.carregando.set(false);
          // Tag que sumiu (ex.: excluída depois de um rascunho) não pode seguir escolhida.
          const conhecidas = new Set(lista.map((t) => t.id));
          if (this.ids().some((id) => !conhecidas.has(id))) {
            this.ids.update((l) => l.filter((id) => conhecidas.has(id)));
          }
        },
        error: (error: unknown) => {
          this.carregando.set(false);
          this.erroCarga.set(problemMessage(error));
        },
      });
  }

  protected readonly mensagemAcerto = MENSAGEM_DUAS_DE_ACERTO;
}
