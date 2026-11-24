import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiClient } from '../../core/api/api-client';
import { problemMessage } from '../../core/errors/problem-details';

/** Aplicativo conectado (`GET /me/connected-apps`, 008 FR-003). `id` é o da autorização. */
export interface AplicativoConectado {
  id: string;
  name: string;
  connectedAt: string;
  lastUsedAt: string | null;
}

/** "24/11/2026 10:30": data e hora no fuso do aparelho. */
export function formatarDataHora(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

/** Seção "Aplicativos conectados" do `/perfil` (008 US1.2): quem está conectado, desde quando, último uso, e Desconectar. */
@Component({
  selector: 'app-aplicativos-conectados',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './aplicativos-conectados.html',
  styleUrl: './aplicativos-conectados.scss',
})
export class AplicativosConectados {
  private readonly api = inject(ApiClient);

  protected readonly aplicativos = signal<AplicativoConectado[] | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly confirmando = signal<string | null>(null);
  protected readonly desconectando = signal<string | null>(null);
  protected readonly formatar = formatarDataHora;

  constructor() {
    this.api.get<AplicativoConectado[]>('/me/connected-apps').subscribe({
      next: (lista) => this.aplicativos.set(lista),
      error: (error: unknown) => {
        this.aplicativos.set([]);
        this.erro.set(problemMessage(error));
      },
    });
  }

  protected pedirConfirmacao(id: string): void {
    this.confirmando.set(id);
  }

  protected manter(): void {
    this.confirmando.set(null);
  }

  protected desconectar(id: string): void {
    if (this.desconectando()) return;
    this.desconectando.set(id);
    this.erro.set(null);
    this.api.delete<void>(`/me/connected-apps/${encodeURIComponent(id)}`).subscribe({
      next: () => {
        this.desconectando.set(null);
        this.confirmando.set(null);
        this.aplicativos.update((lista) => (lista ?? []).filter((a) => a.id !== id));
      },
      error: (error: unknown) => {
        this.desconectando.set(null);
        this.erro.set(problemMessage(error));
      },
    });
  }
}
