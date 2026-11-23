import { Routes } from '@angular/router';
import { today } from './core/money';
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
    path: 'esqueci-senha',
    canActivate: [publicGuard],
    loadComponent: () =>
      import('./features/auth/esqueci-senha/esqueci-senha.page').then((m) => m.EsqueciSenhaPage),
  },
  {
    // Sem publicGuard: o link do e-mail precisa funcionar mesmo com outra sessão aberta.
    path: 'redefinir-senha',
    loadComponent: () =>
      import('./features/auth/redefinir-senha/redefinir-senha.page').then(
        (m) => m.RedefinirSenhaPage,
      ),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        pathMatch: 'full',
        redirectTo: () => `/mes/${today().slice(0, 7)}`,
      },
      {
        path: 'mes',
        pathMatch: 'full',
        redirectTo: () => `/mes/${today().slice(0, 7)}`,
      },
      {
        path: 'mes/:mes',
        loadComponent: () => import('./features/mes/mes.page').then((m) => m.MesPage),
      },
      {
        path: 'carteiras',
        loadComponent: () =>
          import('./features/carteiras/carteiras.page').then((m) => m.CarteirasPage),
      },
      {
        path: 'carteiras/nova',
        loadComponent: () =>
          import('./features/carteiras/carteira-form.page').then((m) => m.CarteiraFormPage),
      },
      {
        path: 'carteiras/:id',
        loadComponent: () =>
          import('./features/carteiras/carteira-form.page').then((m) => m.CarteiraFormPage),
      },
      {
        path: 'perfil',
        loadComponent: () => import('./features/perfil/perfil.page').then((m) => m.PerfilPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
