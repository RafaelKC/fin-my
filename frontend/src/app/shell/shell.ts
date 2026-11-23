import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';

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

  protected readonly itens: readonly ItemDeMenu[] = [
    { rotulo: 'Mês', icone: 'calendar_month', rota: '/' },
    { rotulo: 'Perfil', icone: 'person', rota: '/perfil' },
  ];
}
