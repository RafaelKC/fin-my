import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Rota logada: sem sessão → `/entrar`, guardando a rota pedida (`returnUrl`). */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.restore();
  if (auth.isAuthenticated()) return true;
  const queryParams = state.url !== '/' ? { returnUrl: state.url } : {};
  return router.createUrlTree(['/entrar'], { queryParams });
};

/** Rota pública: com sessão ativa → `/`. */
export const publicGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.restore();
  return auth.isAuthenticated() ? router.createUrlTree(['/']) : true;
};
