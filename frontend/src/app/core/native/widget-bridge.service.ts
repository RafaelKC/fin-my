import { Injectable, InjectionToken, effect, inject } from '@angular/core';
import { Capacitor, registerPlugin } from '@capacitor/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { AuthService } from '../auth/auth.service';

/** Plugin nativo do app Android (010, `WidgetBridgePlugin.kt`). */
export interface WidgetBridgePlugin {
  status(): Promise<{ connected: boolean }>;
  begin(): Promise<{ clientId: string; redirectUri: string; codeChallenge: string }>;
  complete(options: { ticket: string }): Promise<{ connected: boolean }>;
  disconnect(): Promise<void>;
  setAccent(options: { id: string }): Promise<void>;
  /** Leitura de notificações de banco (010). */
  notificationStatus(): Promise<{
    listenerEnabled: boolean;
    apps: { package: string; label: string; bank: string | null; enabled: boolean }[];
  }>;
  openNotificationAccess(): Promise<void>;
  setBankApp(options: { package: string; enabled: boolean }): Promise<void>;
}

/** O plugin quando o front roda dentro do app Android; `null` no navegador. */
export const WIDGET_BRIDGE = new InjectionToken<WidgetBridgePlugin | null>('WIDGET_BRIDGE', {
  providedIn: 'root',
  factory: () =>
    Capacitor.isNativePlatform() ? registerPlugin<WidgetBridgePlugin>('WidgetBridge') : null,
});

/**
 * Conecta o widget à sessão do app, sem segundo login (010): com a pessoa logada dentro do app Android, pede ao nativo
 * o `code_challenge`, cria o ticket de consentimento do cliente `finmy-android` com a sessão do app e entrega o ticket ao
 * nativo, que o troca por tokens. No navegador não faz nada.
 */
@Injectable({ providedIn: 'root' })
export class WidgetBridgeService {
  private readonly bridge = inject(WIDGET_BRIDGE);
  private readonly api = inject(ApiClient);
  private readonly auth = inject(AuthService);
  private conectando: Promise<void> | null = null;

  constructor() {
    if (!this.bridge) return;
    let estavaLogado = false;
    effect(() => {
      const logado = this.auth.isAuthenticated();
      if (logado) void this.conectar();
      // Saiu do app (logout ou sessão encerrada): o widget perde o acesso junto.
      else if (estavaLogado) void this.desconectar();
      estavaLogado = logado;
    });
  }

  /** Conecta se ainda não estiver conectado; falhas ficam para a próxima abertura do app. */
  conectar(): Promise<void> {
    this.conectando ??= this.tentar().finally(() => (this.conectando = null));
    return this.conectando;
  }

  /** Ao sair do app: o widget deixa de ter acesso. */
  async desconectar(): Promise<void> {
    await this.bridge?.disconnect().catch(() => undefined);
  }

  private async tentar(): Promise<void> {
    const bridge = this.bridge;
    if (!bridge) return;
    try {
      if ((await bridge.status()).connected) return;
      const pedido = await bridge.begin();
      const { ticket } = await firstValueFrom(
        this.api.post<{ ticket: string }>('/oauth/consent-tickets', {
          clientId: pedido.clientId,
          redirectUri: pedido.redirectUri,
          codeChallenge: pedido.codeChallenge,
        }),
      );
      await bridge.complete({ ticket });
    } catch {
      // Sem rede ou sessão expirada: tenta de novo na próxima vez que a sessão começar.
    }
  }
}
