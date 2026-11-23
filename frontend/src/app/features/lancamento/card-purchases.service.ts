import { Injectable, inject } from '@angular/core';
import { Observable, Subject, tap } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import {
  CardPurchase,
  CardPurchaseCreate,
  CardPurchaseResult,
  CardPurchaseUpdate,
  EscopoCompra,
} from './card-purchase.models';
import { DeletionResult } from './entry.models';

/** Acesso à API de compras no cartão (`/api/v1/card-purchases`, contracts/openapi.yaml). */
@Injectable({ providedIn: 'root' })
export class CardPurchasesService {
  private readonly api = inject(ApiClient);
  private readonly criadasSubject = new Subject<CardPurchaseResult>();

  /** Emite cada criação (à vista, parcelas ou em andamento) para as telas recarregarem. */
  readonly criadas$ = this.criadasSubject.asObservable();

  /** `idempotencyKey`: uma por formulário, reaproveitada só em reenvio idêntico (research R2). */
  create(input: CardPurchaseCreate, idempotencyKey: string): Observable<CardPurchaseResult> {
    return this.api
      .post<CardPurchaseResult>('/card-purchases', input, { 'Idempotency-Key': idempotencyKey })
      .pipe(tap((resultado) => this.criadasSubject.next(resultado)));
  }

  get(id: string): Observable<CardPurchase> {
    return this.api.get<CardPurchase>(`/card-purchases/${id}`);
  }

  update(id: string, corpo: CardPurchaseUpdate): Observable<CardPurchase> {
    return this.api.put<CardPurchase>(`/card-purchases/${id}`, corpo);
  }

  /** Exclui (RT-09); devolve o lote para o "Desfazer". */
  delete(id: string, scope: EscopoCompra = 'this'): Observable<DeletionResult> {
    return this.api.delete<DeletionResult>(`/card-purchases/${id}?scope=${scope}`);
  }

  setReconciled(id: string, reconciled: boolean): Observable<CardPurchase> {
    return this.api.patch<CardPurchase>(`/card-purchases/${id}/reconciled`, { reconciled });
  }

  /** FR-028: para a fatura anterior ou seguinte; em parcela, só esta ou esta e as seguintes. */
  move(
    id: string,
    direction: 'previous' | 'next',
    scope: 'this' | 'this_and_following' = 'this',
  ): Observable<CardPurchase[]> {
    return this.api.post<CardPurchase[]>(`/card-purchases/${id}/move`, { direction, scope });
  }
}
