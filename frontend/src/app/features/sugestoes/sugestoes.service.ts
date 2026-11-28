import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';

/** Sugestão lida de uma notificação de banco no celular (010). */
export interface Sugestao {
  id: string;
  source: string;
  bank: string | null;
  kind: 'expense' | 'income' | 'card_purchase';
  description: string;
  amountCents: number;
  amountText: string;
  occurredAt: string;
  cardLast4: string | null;
  status: string;
  suggestedCreditCardId: string | null;
  suggestedWalletId: string | null;
}

@Injectable({ providedIn: 'root' })
export class SugestoesService {
  private readonly api = inject(ApiClient);

  listar(): Observable<Sugestao[]> {
    return this.api.get<Sugestao[]>('/suggestions');
  }

  obter(id: string): Observable<Sugestao> {
    return this.api.get<Sugestao>(`/suggestions/${id}`);
  }

  lancada(id: string, linkedId: string | null): Observable<void> {
    return this.api.post<void>(`/suggestions/${id}/added`, { linkedId });
  }

  descartar(id: string): Observable<void> {
    return this.api.post<void>(`/suggestions/${id}/dismiss`);
  }
}
