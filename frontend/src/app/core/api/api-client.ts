import { HttpClient, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Prefixo de todas as rotas da API (mesma origem; o nginx faz o proxy). */
export const API_BASE = '/api/v1';

/** Cabeçalhos extras de uma chamada (ex.: `Idempotency-Key`). */
export type ApiHeaders = Record<string, string>;

/** Cliente HTTP fino: monta a URL a partir de `/api/v1` e aceita cabeçalhos extras. */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  get<T>(path: string): Observable<T> {
    return this.http.get<T>(url(path));
  }

  /** Baixa um arquivo (corpo binário); a resposta inteira vem junto para ler `Content-Disposition`. */
  getFile(path: string): Observable<HttpResponse<Blob>> {
    return this.http.get(url(path), { observe: 'response', responseType: 'blob' });
  }

  post<T>(path: string, body?: unknown, headers?: ApiHeaders): Observable<T> {
    return this.http.post<T>(url(path), body ?? null, options(headers));
  }

  patch<T>(path: string, body: unknown): Observable<T> {
    return this.http.patch<T>(url(path), body);
  }

  put<T>(path: string, body: unknown, headers?: ApiHeaders): Observable<T> {
    return this.http.put<T>(url(path), body, options(headers));
  }

  delete<T>(path: string): Observable<T> {
    return this.http.delete<T>(url(path));
  }
}

export function url(path: string): string {
  return `${API_BASE}${path.startsWith('/') ? path : `/${path}`}`;
}

function options(headers?: ApiHeaders): { headers?: HttpHeaders } {
  return headers ? { headers: new HttpHeaders(headers) } : {};
}
