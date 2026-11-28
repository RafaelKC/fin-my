import { Injectable, computed, signal } from '@angular/core';
import { NomeIcone } from '@/shared/ui/icone';

export interface ItemDeMenu {
  id: string;
  rotulo: string;
  icone: NomeIcone;
  rota: string;
}

/** Telas que podem ir para a barra ou para o "Mais" (o Perfil fica sempre no fim do "Mais"). */
export const ITENS_DE_MENU: readonly ItemDeMenu[] = [
  { id: 'mes', rotulo: 'Mês', icone: 'calendar_month', rota: '/mes' },
  { id: 'cartoes', rotulo: 'Cartões', icone: 'credit_card', rota: '/cartoes' },
  { id: 'acertos', rotulo: 'Acertos', icone: 'handshake', rota: '/acertos' },
  { id: 'painel', rotulo: 'Painel', icone: 'dashboard', rota: '/painel' },
  { id: 'planejamento', rotulo: 'Planejamento', icone: 'event_note', rota: '/planejamento' },
  { id: 'carteiras', rotulo: 'Carteiras', icone: 'account_balance_wallet', rota: '/carteiras' },
  { id: 'recorrencias', rotulo: 'Recorrências', icone: 'repeat', rota: '/recorrencias' },
  { id: 'busca', rotulo: 'Busca', icone: 'search', rota: '/busca' },
  { id: 'tags', rotulo: 'Tags', icone: 'sell', rota: '/tags' },
  { id: 'sugestoes', rotulo: 'Sugestões', icone: 'inbox', rota: '/sugestoes' },
];

export const PERFIL: ItemDeMenu = {
  id: 'perfil',
  rotulo: 'Perfil',
  icone: 'person',
  rota: '/perfil',
};

/** No máximo 4 na barra: dois antes do "+" e dois depois. */
export const MAX_NA_BARRA = 4;

export interface ConfigNavegacao {
  /** Todos os ids, na ordem escolhida (barra e "Mais" seguem esta ordem). */
  ordem: string[];
  /** Ids que ficam na barra (até [MAX_NA_BARRA]). */
  naBarra: string[];
}

export const PADRAO: ConfigNavegacao = {
  ordem: ITENS_DE_MENU.map((i) => i.id),
  naBarra: ['mes', 'cartoes', 'acertos'],
};

const CHAVE = 'finmy.navegacao';

/** Normaliza uma configuração lida do aparelho: ids conhecidos, todos presentes, no máximo 4 na barra. */
export function normalizar(config: Partial<ConfigNavegacao> | null | undefined): ConfigNavegacao {
  const ids = ITENS_DE_MENU.map((i) => i.id);
  const ordem = [...new Set((config?.ordem ?? []).filter((id) => ids.includes(id)))];
  for (const id of ids) if (!ordem.includes(id)) ordem.push(id);
  const naBarra = ordem
    .filter((id) => (config?.naBarra ?? PADRAO.naBarra).includes(id))
    .slice(0, MAX_NA_BARRA);
  return { ordem, naBarra };
}

/**
 * Barra inferior / menu personalizável (Perfil › Navegação), por aparelho: quais telas ficam na barra (até 4) e a ordem;
 * as demais vão para o "Mais". O menu lateral do computador segue a mesma ordem.
 */
@Injectable({ providedIn: 'root' })
export class NavegacaoService {
  readonly config = signal<ConfigNavegacao>(this.ler());

  private readonly porId = new Map(ITENS_DE_MENU.map((i) => [i.id, i]));
  private readonly barra = computed(() =>
    this.config()
      .ordem.filter((id) => this.config().naBarra.includes(id))
      .map((id) => this.porId.get(id)!),
  );
  /** Itens antes do "+" (até 2). */
  readonly esquerda = computed(() => this.barra().slice(0, 2));
  /** Itens depois do "+" (até 2). */
  readonly direita = computed(() => this.barra().slice(2));
  /** Itens do "Mais", na ordem, com o Perfil no fim. */
  readonly mais = computed(() => [
    ...this.config()
      .ordem.filter((id) => !this.config().naBarra.includes(id))
      .map((id) => this.porId.get(id)!),
    PERFIL,
  ]);
  /** Todos os itens na ordem escolhida (lista do Perfil). */
  readonly itens = computed(() => this.config().ordem.map((id) => this.porId.get(id)!));

  naBarra(id: string): boolean {
    return this.config().naBarra.includes(id);
  }

  /** Põe ou tira da barra; com 4 na barra, não põe mais nenhum. */
  alternarBarra(id: string): boolean {
    const atual = this.config();
    const esta = atual.naBarra.includes(id);
    if (!esta && atual.naBarra.length >= MAX_NA_BARRA) return false;
    this.salvar({
      ...atual,
      naBarra: esta ? atual.naBarra.filter((x) => x !== id) : [...atual.naBarra, id],
    });
    return true;
  }

  /** Sobe (-1) ou desce (+1) um item na ordem. */
  mover(id: string, passo: -1 | 1): void {
    const ordem = [...this.config().ordem];
    const i = ordem.indexOf(id);
    const j = i + passo;
    if (i < 0 || j < 0 || j >= ordem.length) return;
    [ordem[i], ordem[j]] = [ordem[j], ordem[i]];
    this.salvar({ ...this.config(), ordem });
  }

  restaurar(): void {
    this.salvar(PADRAO);
  }

  private salvar(config: ConfigNavegacao): void {
    const normal = normalizar(config);
    this.config.set(normal);
    try {
      localStorage.setItem(CHAVE, JSON.stringify(normal));
    } catch {
      // sem armazenamento: vale nesta sessão
    }
  }

  private ler(): ConfigNavegacao {
    try {
      const texto = localStorage.getItem(CHAVE);
      return normalizar(texto ? (JSON.parse(texto) as Partial<ConfigNavegacao>) : PADRAO);
    } catch {
      return normalizar(PADRAO);
    }
  }
}
