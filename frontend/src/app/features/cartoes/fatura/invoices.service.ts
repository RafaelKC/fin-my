import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../../core/api/api-client';
import { DeletionResult } from '../../lancamento/entry.models';
import {
  Invoice,
  InvoicePayment,
  InvoicePaymentCreate,
  InvoicePaymentUpdate,
} from './invoice.models';

/** Acesso à API de faturas e pagamentos (contracts/openapi.yaml). */
@Injectable({ providedIn: 'root' })
export class InvoicesService {
  private readonly api = inject(ApiClient);

  get(cardId: string, month: string): Observable<Invoice> {
    return this.api.get<Invoice>(`/credit-cards/${cardId}/invoices/${month}`);
  }

  /** `idempotencyKey`: uma por tentativa de pagamento (corpo vazio = padrões). */
  pay(
    cardId: string,
    month: string,
    corpo: InvoicePaymentCreate,
    idempotencyKey: string,
  ): Observable<Invoice> {
    return this.api.post<Invoice>(`/credit-cards/${cardId}/invoices/${month}/payments`, corpo, {
      'Idempotency-Key': idempotencyKey,
    });
  }

  /** Desmarcar a caixinha: exclui todos os pagamentos da fatura (FR-019). */
  deletePayments(cardId: string, month: string): Observable<DeletionResult> {
    return this.api.delete<DeletionResult>(`/credit-cards/${cardId}/invoices/${month}/payments`);
  }

  /** FR-020: leva o restante para a próxima fatura. */
  carryOver(cardId: string, month: string): Observable<Invoice> {
    return this.api.post<Invoice>(`/credit-cards/${cardId}/invoices/${month}/carry-over`);
  }

  updatePayment(id: string, corpo: InvoicePaymentUpdate): Observable<InvoicePayment> {
    return this.api.put<InvoicePayment>(`/invoice-payments/${id}`, corpo);
  }

  deletePayment(id: string): Observable<DeletionResult> {
    return this.api.delete<DeletionResult>(`/invoice-payments/${id}`);
  }
}
