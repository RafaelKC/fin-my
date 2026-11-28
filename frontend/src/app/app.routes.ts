import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { DevicePrefsService } from './core/prefs/device-prefs.service';
import { today } from './core/money';
import { authGuard, publicGuard } from './core/auth/auth.guards';
import { etapaInicial, rotaDaEtapa } from './features/primeiros-passos/onboarding-logic';
import { OnboardingService } from './features/primeiros-passos/onboarding.service';

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
    // Consentimento do conector do Claude (008): exige login e guarda os parâmetros do pedido em `returnUrl`; sem o shell.
    path: 'autorizar',
    canActivate: [authGuard],
    loadComponent: () => import('./features/autorizar/autorizar.page').then((m) => m.AutorizarPage),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      {
        // Tela inicial do aparelho (006 FR-006): Mês (padrão) ou Painel; um Espaço novo abre o guia (FR-007).
        path: '',
        pathMatch: 'full',
        redirectTo: () => {
          const inicial =
            inject(DevicePrefsService).homeScreen() === 'painel'
              ? `/painel/${today().slice(0, 4)}`
              : `/mes/${today().slice(0, 7)}`;
          return inject(OnboardingService)
            .get()
            .pipe(
              map((estado) => (estado.autoStart ? rotaDaEtapa(etapaInicial(estado)) : inicial)),
              catchError(() => of(inicial)),
            );
        },
      },
      {
        path: 'primeiros-passos',
        pathMatch: 'full',
        redirectTo: () =>
          inject(OnboardingService)
            .get()
            .pipe(
              map((estado) => rotaDaEtapa(etapaInicial(estado))),
              catchError(() => of('/primeiros-passos/carteiras')),
            ),
      },
      {
        path: 'primeiros-passos/:etapa',
        loadComponent: () =>
          import('./features/primeiros-passos/primeiros-passos.page').then(
            (m) => m.PrimeirosPassosPage,
          ),
      },
      {
        path: 'painel',
        pathMatch: 'full',
        redirectTo: () => `/painel/${today().slice(0, 4)}`,
      },
      {
        path: 'painel/:ano',
        loadComponent: () => import('./features/painel/painel.page').then((m) => m.PainelPage),
      },
      {
        path: 'planejamento',
        pathMatch: 'full',
        redirectTo: () => `/planejamento/${today().slice(0, 4)}`,
      },
      {
        path: 'planejamento/:ano',
        loadComponent: () =>
          import('./features/planejamento/planejamento.page').then((m) => m.PlanejamentoPage),
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
        path: 'acertos',
        pathMatch: 'full',
        redirectTo: () => `/acertos/${today().slice(0, 7)}`,
      },
      {
        path: 'acertos/:mes',
        loadComponent: () => import('./features/acertos/acertos.page').then((m) => m.AcertosPage),
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
        path: 'cartoes',
        loadComponent: () => import('./features/cartoes/cartoes.page').then((m) => m.CartoesPage),
      },
      {
        path: 'cartoes/novo',
        loadComponent: () =>
          import('./features/cartoes/cartao-form.page').then((m) => m.CartaoFormPage),
      },
      {
        path: 'cartoes/:id/editar',
        loadComponent: () =>
          import('./features/cartoes/cartao-form.page').then((m) => m.CartaoFormPage),
      },
      {
        path: 'cartoes/:id/faturas/:mes',
        loadComponent: () =>
          import('./features/cartoes/fatura/fatura.page').then((m) => m.FaturaPage),
      },
      {
        path: 'recorrencias',
        loadComponent: () =>
          import('./features/recorrencias/recorrencias.page').then((m) => m.RecorrenciasPage),
      },
      {
        path: 'recorrencias/nova',
        loadComponent: () =>
          import('./features/recorrencias/recorrencia-form.page').then(
            (m) => m.RecorrenciaFormPage,
          ),
      },
      {
        path: 'recorrencias/:id',
        loadComponent: () =>
          import('./features/recorrencias/recorrencia-form.page').then(
            (m) => m.RecorrenciaFormPage,
          ),
      },
      {
        path: 'sugestoes',
        loadComponent: () =>
          import('./features/sugestoes/sugestoes.page').then((m) => m.SugestoesPage),
      },
      {
        path: 'sugestoes/:id',
        loadComponent: () =>
          import('./features/sugestoes/sugestoes.page').then((m) => m.SugestoesPage),
      },
      {
        path: 'busca',
        loadComponent: () => import('./features/busca/busca.page').then((m) => m.BuscaPage),
      },
      {
        path: 'tags',
        loadComponent: () => import('./features/tags/tags.page').then((m) => m.TagsPage),
      },
      {
        path: 'perfil',
        loadComponent: () => import('./features/perfil/perfil.page').then((m) => m.PerfilPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
