import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { LancamentoLauncher } from '../features/lancamento/lancamento.launcher';

interface ItemDeMenu {
  rotulo: string;
  icone: string;
  rota: string;
}

/**
 * Casca do app logado: nome do Espaço no topo; navegação na barra inferior (celular) ou no menu
 * lateral (≥ 1024 px). Uma única `<nav>`; o CSS muda a disposição.
 */
@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatIconModule],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  protected readonly espaco = inject(AuthService).tenant;

  private readonly lancamento = inject(LancamentoLauncher);

  protected readonly itens: readonly ItemDeMenu[] = [
    { rotulo: 'Mês', icone: 'calendar_month', rota: '/mes' },
    { rotulo: 'Carteiras', icone: 'account_balance_wallet', rota: '/carteiras' },
    { rotulo: 'Busca', icone: 'search', rota: '/busca' },
    { rotulo: 'Perfil', icone: 'person', rota: '/perfil' },
  ];

  /** "+" global: abre o formulário rápido de qualquer tela logada (FR-008). */
  protected lancar(): void {
    this.lancamento.abrir();
  }
}
