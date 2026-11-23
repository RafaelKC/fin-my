import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { Wallet, WalletInput } from './wallet.models';

export type FiltroArquivadas = 'false' | 'true' | 'all';

/** Acesso à API de carteiras (`/api/v1/wallets`, contracts/openapi.yaml). */
@Injectable({ providedIn: 'root' })
export class WalletsService {
  private readonly api = inject(ApiClient);

  list(archived: FiltroArquivadas = 'false'): Observable<Wallet[]> {
    return this.api.get<Wallet[]>(`/wallets?archived=${archived}`);
  }

  get(id: string): Observable<Wallet> {
    return this.api.get<Wallet>(`/wallets/${id}`);
  }

  create(input: WalletInput): Observable<Wallet> {
    return this.api.post<Wallet>('/wallets', input);
  }

  update(id: string, input: WalletInput, version: number): Observable<Wallet> {
    return this.api.put<Wallet>(`/wallets/${id}`, { ...input, version });
  }

  makePrimary(id: string): Observable<Wallet> {
    return this.api.post<Wallet>(`/wallets/${id}/make-primary`);
  }

  archive(id: string, newPrimaryWalletId?: string): Observable<Wallet> {
    return this.api.post<Wallet>(
      `/wallets/${id}/archive`,
      newPrimaryWalletId ? { newPrimaryWalletId } : {},
    );
  }

  unarchive(id: string): Observable<Wallet> {
    return this.api.post<Wallet>(`/wallets/${id}/unarchive`);
  }

  delete(id: string, newPrimaryWalletId?: string): Observable<void> {
    const query = newPrimaryWalletId
      ? `?newPrimaryWalletId=${encodeURIComponent(newPrimaryWalletId)}`
      : '';
    return this.api.delete<void>(`/wallets/${id}${query}`);
  }
}
