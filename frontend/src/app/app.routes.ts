import { Routes } from '@angular/router';
import { authGuard, publicGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  {
    path: 'entrar',
    canActivate: [publicGuard],
    loadComponent: () => import('./features/auth/entrar/entrar.page').then((m) => m.EntrarPage),
  },
  {
    path: 'criar-conta',
    canActivate: [publicGuard],
    loadComponent: () =>
      import('./features/auth/criar-conta/criar-conta.page').then((m) => m.CriarContaPage),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./features/mes/mes.page').then((m) => m.MesPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
