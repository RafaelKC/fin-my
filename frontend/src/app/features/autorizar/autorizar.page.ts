import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ApiClient } from '../../core/api/api-client';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/errors/problem-details';
import { PageNavigator } from '../../core/navigation/page-navigator';

/** Parâmetros que só o Fin-My acrescenta; nunca são repassados do pedido original. */
const PARAMETROS_PROPRIOS = ['consent_ticket', 'consent_denied'];

/** Cliente do app Android (009 FR-006): texto próprio; qualquer outro cliente é tratado como o Claude (008). */
export const CLIENTE_ANDROID = 'finmy-android';

/** Título e permissão mostrados no consentimento, conforme o cliente que pediu a autorização. */
export interface TextoDeConsentimento {
  titulo: string;
  nome: string;
  permissao: string;
  /** Fecha a frase depois do nome do Espaço, antes do ponto. */
  complemento: string;
}

export function textoDoCliente(clientId: string | null): TextoDeConsentimento {
  return clientId === CLIENTE_ANDROID
    ? {
        titulo: 'Autorizar o App Fin-My (Android)',
        nome: 'O App Fin-My (Android)',
        permissao: 'poderá ver seus cartões e faturas',
        complemento: ' para mostrar no widget',
      }
    : {
        titulo: 'Autorizar o Claude',
        nome: 'O Claude',
        permissao: 'poderá ver e registrar seus lançamentos',
        complemento: '',
      };
}

/**
 * Tela `/autorizar` (008 US1, research R2): o Claude mandou a pessoa para cá (via `/oauth/authorize`). O `authGuard` garante o
 * login preservando os parâmetros. "Autorizar" pede o ticket de consentimento e "Cancelar" nega; os dois terminam em
 * navegação de página inteira para `/oauth/authorize`, que devolve a pessoa ao Claude.
 */
@Component({
  selector: 'app-autorizar-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './autorizar.page.html',
  styleUrl: './autorizar.page.scss',
})
export class AutorizarPage {
  private readonly api = inject(ApiClient);
  private readonly navigator = inject(PageNavigator);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;

  protected readonly texto = textoDoCliente(this.query.get('client_id'));
  protected readonly android = this.query.get('client_id') === CLIENTE_ANDROID;
  protected readonly espaco = inject(AuthService).tenant()?.name ?? '';
  protected readonly pedidoValido =
    !!this.query.get('client_id') &&
    !!this.query.get('redirect_uri') &&
    !!this.query.get('code_challenge');

  protected readonly enviando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected autorizar(): void {
    if (this.enviando() || !this.pedidoValido) return;
    this.enviando.set(true);
    this.erro.set(null);
    this.api
      .post<{ ticket: string }>('/oauth/consent-tickets', {
        clientId: this.query.get('client_id'),
        redirectUri: this.query.get('redirect_uri'),
        codeChallenge: this.query.get('code_challenge'),
      })
      .subscribe({
        next: ({ ticket }) => this.navigator.go(this.urlDeAutorizacao('consent_ticket', ticket)),
        error: (error: unknown) => {
          this.enviando.set(false);
          this.erro.set(problemMessage(error));
        },
      });
  }

  protected cancelar(): void {
    if (this.enviando() || !this.pedidoValido) return;
    this.enviando.set(true);
    this.navigator.go(this.urlDeAutorizacao('consent_denied', '1'));
  }

  /** `/oauth/authorize` com exatamente os parâmetros do pedido original mais o `extra`. */
  private urlDeAutorizacao(chave: string, valor: string): string {
    const parametros = new URLSearchParams();
    for (const nome of this.query.keys) {
      if (PARAMETROS_PROPRIOS.includes(nome)) continue;
      for (const v of this.query.getAll(nome)) parametros.append(nome, v);
    }
    parametros.append(chave, valor);
    return `/oauth/authorize?${parametros.toString()}`;
  }
}
