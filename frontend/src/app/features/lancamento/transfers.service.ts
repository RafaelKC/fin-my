import { Injectable, inject } from '@angular/core';
import { Observable, Subject, tap } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { Transfer } from '../mes/month.models';
import { TransferCreate } from './entry.models';

/** Acesso à API de transferências (`/api/v1/transfers`, contracts/openapi.yaml). */
@Injectable({ providedIn: 'root' })
export class TransfersService {
  private readonly api = inject(ApiClient);
  private readonly criadasSubject = new Subject<Transfer>();

  /** Emite cada transferência criada, para as telas (ex.: visão do mês, FR-016) recarregarem. */
  readonly criadas$ = this.criadasSubject.asObservable();

  /** `idempotencyKey`: uma por formulário, reaproveitada em reenvios (research R2, R12). */
  create(input: TransferCreate, idempotencyKey: string): Observable<Transfer> {
    return this.api
      .post<Transfer>('/transfers', input, { 'Idempotency-Key': idempotencyKey })
      .pipe(tap((transfer) => this.criadasSubject.next(transfer)));
  }
}
