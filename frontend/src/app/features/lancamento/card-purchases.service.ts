import { Injectable, inject } from '@angular/core';
import { Observable, Subject, tap } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { CardPurchaseCreate, CardPurchaseResult } from './card-purchase.models';

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
}
