import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  Observable,
  catchError,
  finalize,
  firstValueFrom,
  shareReplay,
  tap,
  throwError,
} from 'rxjs';
import { url } from '../api/api-client';
import { SessionResponse, TenantDto, UserDto } from './session';

/**
 * Sessão do app (research R4). O access token vive só em memória: o refresh token é um cookie
 * HttpOnly que o navegador envia sozinho a `/auth/refresh`.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<SessionResponse | null>(null);
  private restoring: Promise<void> | null = null;
  private refreshing: Observable<SessionResponse> | null = null;

  readonly user = computed(() => this.session()?.user ?? null);
  readonly tenant = computed(() => this.session()?.tenant ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);

  get accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  /** Guarda a sessão devolvida por signup/login/refresh. */
  startSession(session: SessionResponse): void {
    this.session.set(session);
  }

  /**
   * Na abertura do app: tenta `/auth/refresh`. Sucesso → sessão ativa; qualquer falha → sem sessão
   * (o guard manda para `/entrar`). Executa uma única vez.
   */
  restore(): Promise<void> {
    this.restoring ??= firstValueFrom(this.requestRefresh()).then(
      () => undefined,
      () => undefined,
    );
    return this.restoring;
  }

  /**
   * Renova o access token. Chamadas simultâneas compartilham UMA requisição (o refresh token é
   * rotativo: duas chamadas revogariam a família). Se o servidor responder 401, a sessão acabou.
   */
  refresh(): Observable<SessionResponse> {
    this.refreshing ??= this.requestRefresh().pipe(
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) this.endSession();
        return throwError(() => error);
      }),
      finalize(() => (this.refreshing = null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.refreshing;
  }

  /** Reflete na sessão em memória o nome do usuário/do Espaço alterados em `/perfil`. */
  updateProfile(changes: { user?: UserDto; tenant?: TenantDto }): void {
    this.session.update((s) => (s ? { ...s, ...changes } : s));
  }

  /**
   * Sair neste aparelho (US5): `POST /auth/logout` apaga o cookie e revoga a sessão; é idempotente.
   * Mesmo se a chamada falhar, a sessão local é descartada — o usuário pediu para sair.
   */
  logout(): Observable<void> {
    return this.http.post<void>(url('/auth/logout'), null).pipe(
      finalize(() => {
        this.session.set(null);
        void this.router.navigate(['/entrar']);
      }),
    );
  }

  /** Esquece a sessão local e leva ao login, guardando a rota atual para voltar depois. */
  endSession(): void {
    this.session.set(null);
    const returnUrl = this.router.url;
    void this.router.navigate(['/entrar'], {
      queryParams: returnUrl !== '/' && !returnUrl.startsWith('/entrar') ? { returnUrl } : {},
    });
  }

  private requestRefresh(): Observable<SessionResponse> {
    return this.http
      .post<SessionResponse>(url('/auth/refresh'), null)
      .pipe(tap((session) => this.startSession(session)));
  }
}
