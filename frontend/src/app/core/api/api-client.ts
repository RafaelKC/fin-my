import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Prefixo de todas as rotas da API (mesma origem; o nginx faz o proxy). */
export const API_BASE = '/api/v1';

/** Cliente HTTP fino: monta a URL a partir de `/api/v1` e nada mais. */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  get<T>(path: string): Observable<T> {
    return this.http.get<T>(url(path));
  }

  post<T>(path: string, body?: unknown): Observable<T> {
    return this.http.post<T>(url(path), body ?? null);
  }

  patch<T>(path: string, body: unknown): Observable<T> {
    return this.http.patch<T>(url(path), body);
  }

  put<T>(path: string, body: unknown): Observable<T> {
    return this.http.put<T>(url(path), body);
  }

  delete<T>(path: string): Observable<T> {
    return this.http.delete<T>(url(path));
  }
}

export function url(path: string): string {
  return `${API_BASE}${path.startsWith('/') ? path : `/${path}`}`;
}
