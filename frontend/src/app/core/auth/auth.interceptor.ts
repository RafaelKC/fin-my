import {
  HttpErrorResponse,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { API_BASE } from '../api/api-client';
import { asProblem } from '../errors/problem-details';
import { AuthService } from './auth.service';

const AUTH_PATH = `${API_BASE}/auth/`;

/**
 * Anexa o access token e trata 401 (research R4):
 * - `token_expired` → UM refresh (requisições simultâneas esperam o mesmo) e repete a requisição;
 * - `session_revoked` (ou refresh recusado) → encerra a sessão e vai para o login.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(API_BASE) || req.url.startsWith(AUTH_PATH)) return next(req);

  const auth = inject(AuthService);
  return next(withToken(req, auth.accessToken)).pipe(
    catchError((error: unknown) => {
      if (!isUnauthorized(error)) return throwError(() => error);
      const code = asProblem(error)?.code;

      if (code === 'token_expired') {
        return auth
          .refresh()
          .pipe(switchMap((session) => retry(req, next, session.accessToken, auth)));
      }
      if (code === 'session_revoked') auth.endSession();
      return throwError(() => error);
    }),
  );
};

function retry(req: HttpRequest<unknown>, next: HttpHandlerFn, token: string, auth: AuthService) {
  return next(withToken(req, token)).pipe(
    catchError((error: unknown) => {
      if (isUnauthorized(error) && asProblem(error)?.code === 'session_revoked') auth.endSession();
      return throwError(() => error);
    }),
  );
}

function withToken(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}

function isUnauthorized(error: unknown): error is HttpErrorResponse {
  return error instanceof HttpErrorResponse && error.status === 401;
}
