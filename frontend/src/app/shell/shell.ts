import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';
import { AuthService } from '../core/auth/auth.service';
import { LancamentoLauncher } from '../features/lancamento/lancamento.launcher';

interface ItemDeMenu {
  rotulo: string;
  icone: string;
  rota: string;
}

/**
 * Casca do app logado: nome do Espaço no topo; navegação na barra inferior (celular) ou no menu
 * lateral (≥ 1024 px). Uma única `<nav>`; o CSS muda a disposição. No celular a barra é
 * Mês · Cartões · + · Acertos · Mais, e "Mais" abre Carteiras, Recorrências, Busca, Tags e Perfil; no menu lateral todos
 * aparecem direto (contracts/ui-rotas.md).
 */
@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatIconModule],
  host: { '(document:keydown.escape)': 'maisAberto.set(false)' },
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  protected readonly espaco = inject(AuthService).tenant;

  private readonly lancamento = inject(LancamentoLauncher);

  private readonly router = inject(Router);

  protected readonly itens: readonly ItemDeMenu[] = [
    { rotulo: 'Mês', icone: 'calendar_month', rota: '/mes' },
    { rotulo: 'Cartões', icone: 'credit_card', rota: '/cartoes' },
  ];

  protected readonly acertos: ItemDeMenu = {
    rotulo: 'Acertos',
    icone: 'handshake',
    rota: '/acertos',
  };

  /** Itens de "Mais" no celular; no menu lateral aparecem como os demais. */
  protected readonly mais: readonly ItemDeMenu[] = [
    { rotulo: 'Carteiras', icone: 'account_balance_wallet', rota: '/carteiras' },
    { rotulo: 'Recorrências', icone: 'repeat', rota: '/recorrencias' },
    { rotulo: 'Busca', icone: 'search', rota: '/busca' },
    { rotulo: 'Tags', icone: 'sell', rota: '/tags' },
    { rotulo: 'Perfil', icone: 'person', rota: '/perfil' },
  ];

  protected readonly maisAberto = signal(false);

  constructor() {
    // Navegar (por qualquer meio) fecha o painel "Mais".
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.maisAberto.set(false));
  }

  protected alternarMais(): void {
    this.maisAberto.update((v) => !v);
  }

  /** "+" global: abre o formulário rápido de qualquer tela logada (FR-008). */
  protected lancar(): void {
    this.lancamento.abrir();
  }
}
