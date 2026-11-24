import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';
import { problemMessage } from '../../core/errors/problem-details';
import { BrlPipe } from '../../core/money';
import { CardsService } from '../cartoes/cards.service';
import { WalletsService } from '../carteiras/wallets.service';
import {
  RECURRENCE_KINDS,
  REVIEW_MESSAGES,
  Recurrence,
  RecurrenceList,
  RecurrenceStatus,
} from './recurrence.models';
import { RecurrencesService } from './recurrences.service';

/**
 * Tela `/recorrencias` (005 US1, FR-009): ativas e encerradas com valor, dia, carteira/cartão, próxima
 * ocorrência e aviso de revisão; no topo, o total mensal previsto de Entradas e de Saídas + Compras.
 */
@Component({
  selector: 'app-recorrencias-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, BrlPipe],
  templateUrl: './recorrencias.page.html',
  styleUrl: './recorrencias.page.scss',
})
export class RecorrenciasPage {
  private readonly recurrences = inject(RecurrencesService);
  private readonly wallets = inject(WalletsService);
  private readonly cards = inject(CardsService);

  protected readonly situacao = signal<RecurrenceStatus>('active');
  protected readonly carregando = signal(true);
  protected readonly erro = signal<string | null>(null);
  protected readonly lista = signal<RecurrenceList | null>(null);
  private readonly nomes = signal(new Map<string, string>());

  protected readonly itens = computed(() => this.lista()?.items ?? []);

  constructor() {
    this.carregarNomes();
    this.carregar();
  }

  protected escolher(situacao: RecurrenceStatus): void {
    if (situacao === this.situacao()) return;
    this.situacao.set(situacao);
    this.carregar();
  }

  protected tipo(r: Recurrence): string {
    return RECURRENCE_KINDS.find((k) => k.value === r.kind)?.label ?? r.kind;
  }

  protected origem(r: Recurrence): string {
    const id = r.walletId ?? r.creditCardId;
    return (id && this.nomes().get(id)) || '—';
  }

  protected aviso(r: Recurrence): string | null {
    return r.needsReview && r.reviewReason ? REVIEW_MESSAGES[r.reviewReason] : null;
  }

  protected data(iso: string): string {
    const [ano, mes, dia] = iso.split('-');
    return `${dia}/${mes}/${ano}`;
  }

  protected carregar(): void {
    this.carregando.set(true);
    this.erro.set(null);
    this.recurrences.list(this.situacao()).subscribe({
      next: (lista) => {
        this.lista.set(lista);
        this.carregando.set(false);
      },
      error: (error: unknown) => {
        this.carregando.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }

  /** Nomes das carteiras e cartões (inclusive arquivados); se falhar, a lista mostra "—". */
  private carregarNomes(): void {
    this.wallets
      .list('all')
      .pipe(catchError(() => of([])))
      .subscribe((lista) => this.juntarNomes(lista));
    this.cards
      .list('all')
      .pipe(catchError(() => of([])))
      .subscribe((lista) => this.juntarNomes(lista));
  }

  private juntarNomes(itens: { id: string; name: string }[]): void {
    this.nomes.update((atual) => {
      const novo = new Map(atual);
      itens.forEach((i) => novo.set(i.id, i.name));
      return novo;
    });
  }
}
