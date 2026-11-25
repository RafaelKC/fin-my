import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ZardButtonComponent } from '@/shared/components/button';
import { Icone } from '@/shared/ui/icone';
import { BrlPipe, monthLabel } from '../../core/money';
import { problemMessage } from '../../core/errors/problem-details';
import { CreditCardOverview, InvoiceSummary } from './card.models';
import { CardsService } from './cards.service';

/** `2026-03` → `março`. */
export function nomeDoMes(mes: string): string {
  return monthLabel(mes).split(' de ')[0].toLowerCase();
}

/** `2026-03-18` → `18/03`. */
export function diaEMes(data: string): string {
  const [, mes, dia] = data.split('-');
  return `${dia}/${mes}`;
}

/** Tela `/cartoes` (US1, US6; FR-025, FR-026): fatura atual, próxima, em aberto e limite por cartão. */
@Component({
  selector: 'app-cartoes-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ZardButtonComponent, Icone, RouterLink, BrlPipe],
  templateUrl: './cartoes.page.html',
  styleUrl: './cartoes.page.scss',
})
export class CartoesPage {
  private readonly cards = inject(CardsService);

  protected readonly arquivados = signal(false);
  protected readonly carregando = signal(true);
  protected readonly erro = signal<string | null>(null);
  protected readonly cartoes = signal<CreditCardOverview[]>([]);

  protected readonly nomeDoMes = nomeDoMes;
  protected readonly diaEMes = diaEMes;

  constructor() {
    this.carregar();
  }

  protected alternarArquivados(): void {
    this.arquivados.update((v) => !v);
    this.carregar();
  }

  /** Fatura vencida com valor em aberto aparece em vermelho (FR-025). */
  /** Parte do limite já usada, de 0 a 100, para a barra do cartão. */
  protected usoDoLimite(limite: number, disponivel: number): number {
    if (limite <= 0) return 100;
    return Math.min(100, Math.max(0, Math.round(((limite - disponivel) / limite) * 100)));
  }

  protected vencida(fatura: InvoiceSummary): boolean {
    return fatura.status === 'overdue';
  }

  private carregar(): void {
    this.carregando.set(true);
    this.erro.set(null);
    this.cards.list(this.arquivados() ? 'true' : 'false').subscribe({
      next: (lista) => {
        this.cartoes.set(lista);
        this.carregando.set(false);
      },
      error: (error: unknown) => {
        this.carregando.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }
}
