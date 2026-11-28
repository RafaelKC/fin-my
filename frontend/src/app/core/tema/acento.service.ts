import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';
import { WIDGET_BRIDGE } from '../native/widget-bridge.service';

/** Cor de destaque: matiz OKLCH dos tokens do tema e as duas cores (hex) do ícone do app. */
export interface Acento {
  id: string;
  nome: string;
  matiz: number;
  /** Degradê do ícone (favicon), do canto de cima ao de baixo; gera `public/icons/acento-<id>.svg`. */
  icone: [string, string];
}

export const ACENTOS: readonly Acento[] = [
  { id: 'indigo', nome: 'Índigo', matiz: 277, icone: ['#5b4ff0', '#2a8f8a'] },
  { id: 'azul', nome: 'Azul', matiz: 250, icone: ['#1d6fe0', '#0fa3b1'] },
  { id: 'turquesa', nome: 'Turquesa', matiz: 195, icone: ['#0f9aa8', '#2a8f5a'] },
  { id: 'verde', nome: 'Verde', matiz: 150, icone: ['#1f9d55', '#7fb800'] },
  { id: 'ambar', nome: 'Âmbar', matiz: 75, icone: ['#d98e04', '#e05d1f'] },
  { id: 'laranja', nome: 'Laranja', matiz: 45, icone: ['#ef6c1a', '#d63b52'] },
  { id: 'rosa', nome: 'Rosa', matiz: 355, icone: ['#e0407b', '#8b4fe0'] },
  { id: 'roxo', nome: 'Roxo', matiz: 305, icone: ['#9b3fd6', '#4f5bf0'] },
];

/** Mesma chave que `public/tema.js` lê antes da primeira pintura. */
export const CHAVE_ACENTO = 'finmy.acento';

/** Ícone do app no degradê da cor de destaque (`public/icons/acento-<id>.svg`; arquivo, não data: URI, por causa da CSP). */
export function iconeDoAcento(acento: Acento): string {
  return `icons/acento-${acento.id}.svg`;
}

/**
 * Cor de destaque escolhida no Perfil (vale para este aparelho, como o tema claro/escuro): troca o matiz dos tokens
 * (`--acento-h`: botões, links, ícones, gráficos, foco) e o ícone da aba (favicon).
 */
@Injectable({ providedIn: 'root' })
export class AcentoService {
  private readonly documento = inject(DOCUMENT);
  /** No app Android: widgets e relógio seguem a mesma cor. */
  private readonly nativo = inject(WIDGET_BRIDGE);
  readonly atual = signal<Acento>(this.ler());

  constructor() {
    this.aplicar(this.atual());
  }

  escolher(id: string): void {
    const acento = ACENTOS.find((a) => a.id === id) ?? ACENTOS[0];
    try {
      localStorage.setItem(CHAVE_ACENTO, acento.id);
    } catch {
      // sem armazenamento: vale só nesta sessão
    }
    this.atual.set(acento);
    this.aplicar(acento);
  }

  private ler(): Acento {
    try {
      const id = localStorage.getItem(CHAVE_ACENTO);
      return ACENTOS.find((a) => a.id === id) ?? ACENTOS[0];
    } catch {
      return ACENTOS[0];
    }
  }

  private aplicar(acento: Acento): void {
    const html = this.documento.documentElement;
    html.style.setProperty('--acento-h', String(acento.matiz));
    html.setAttribute('data-acento', acento.id);
    void this.nativo?.setAccent({ id: acento.id }).catch(() => undefined);
    this.documento
      .querySelectorAll<HTMLLinkElement>('link[rel="icon"]')
      .forEach((link) => link.remove());
    const link = this.documento.createElement('link');
    link.rel = 'icon';
    link.type = 'image/svg+xml';
    link.href = iconeDoAcento(acento);
    this.documento.head.appendChild(link);
  }
}
