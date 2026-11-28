import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { WIDGET_BRIDGE } from '../../core/native/widget-bridge.service';
import { BancoLogo } from '../../shared/bancos/banco-logo';

interface AppDeBanco {
  package: string;
  label: string;
  bank: string | null;
  enabled: boolean;
}

/**
 * Perfil › Notificações do banco (só no app Android, 010): liberar o acesso às notificações e escolher de quais apps de
 * banco o Fin-My lê as movimentações para sugerir lançamentos.
 */
@Component({
  selector: 'app-notificacoes-banco',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BancoLogo, RouterLink],
  host: { '(document:visibilitychange)': 'atualizar()' },
  template: `
    @if (nativo) {
      <section class="secao" aria-labelledby="titulo-notificacoes">
        <h2 id="titulo-notificacoes">Notificações do banco</h2>
        <p class="ajuda">
          Quando o banco avisa uma compra, Pix ou recebimento, o Fin-My sugere o lançamento já
          preenchido. As que você não lançar ficam em <a routerLink="/sugestoes">Sugestões</a>.
        </p>
        @if (!liberado()) {
          <button
            type="button"
            class="liberar"
            data-testid="liberar-notificacoes"
            (click)="liberar()"
          >
            Permitir acesso às notificações
          </button>
        } @else {
          <p class="ok" data-testid="notificacoes-ok">Leitura ligada.</p>
        }
        @if (apps().length > 0) {
          <ul class="apps">
            @for (app of apps(); track app.package) {
              <li>
                <label>
                  <app-banco-logo [codigo]="app.bank" [tamanho]="28" />
                  <span>{{ app.label }}</span>
                  <input type="checkbox" [checked]="app.enabled" (change)="alternar(app)" />
                </label>
              </li>
            }
          </ul>
        } @else {
          <p class="ajuda">Nenhum app de banco conhecido instalado.</p>
        }
      </section>
    }
  `,
  styles: `
    .liberar {
      min-height: 44px;
      padding: 0 18px;
      border: 0;
      border-radius: 999px;
      background: var(--primary);
      color: var(--primary-foreground);
      font: inherit;
      font-weight: 600;
      cursor: pointer;
    }
    .ok {
      color: var(--primary);
      font-weight: 600;
    }
    .apps {
      list-style: none;
      margin: 12px 0 0;
      padding: 0;
      display: grid;
      gap: 6px;
    }
    .apps label {
      display: flex;
      align-items: center;
      gap: 10px;
      min-height: 48px;
      padding: 4px 12px;
      border: 1px solid var(--border);
      border-radius: 12px;
      background: var(--card);
      cursor: pointer;
    }
    .apps span {
      flex: 1;
    }
  `,
})
export class NotificacoesBanco {
  protected readonly nativo = inject(WIDGET_BRIDGE);
  protected readonly liberado = signal(false);
  protected readonly apps = signal<AppDeBanco[]>([]);

  constructor() {
    this.atualizar();
  }

  /** Relê ao voltar das configurações do Android. */
  atualizar(): void {
    if (!this.nativo || (typeof document !== 'undefined' && document.visibilityState === 'hidden'))
      return;
    void this.nativo.notificationStatus().then(
      (status) => {
        this.liberado.set(status.listenerEnabled);
        this.apps.set(status.apps);
      },
      () => undefined,
    );
  }

  protected liberar(): void {
    void this.nativo?.openNotificationAccess();
  }

  protected alternar(app: AppDeBanco): void {
    const enabled = !app.enabled;
    this.apps.update((lista) =>
      lista.map((a) => (a.package === app.package ? { ...a, enabled } : a)),
    );
    void this.nativo?.setBankApp({ package: app.package, enabled });
  }
}
