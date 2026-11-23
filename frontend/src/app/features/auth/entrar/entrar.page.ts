import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiClient } from '../../../core/api/api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { SessionResponse } from '../../../core/auth/session';
import { asProblem, problemMessage } from '../../../core/errors/problem-details';

export const EMAIL_MAX = 254;
export const SENHA_MAX = 128;

/** Só aceita rotas internas do app (evita redirecionar para outro site por `?returnUrl=`). */
export function rotaDeRetorno(valor: string | null): string {
  return valor !== null && /^\/(?![/\\])/.test(valor) ? valor : '/';
}

/** "14 min 59 s", "45 s": tempo restante do bloqueio. */
export function formatarTempoRestante(segundos: number): string {
  const total = Math.max(0, Math.ceil(segundos));
  const min = Math.floor(total / 60);
  const seg = total % 60;
  return min > 0 ? `${min} min ${seg} s` : `${seg} s`;
}

/** Tela `/entrar` (US2, contracts/ui-rotas.md): e-mail e senha, erro único e aviso de bloqueio. */
@Component({
  selector: 'app-entrar-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, MatIconModule],
  templateUrl: './entrar.page.html',
  styleUrl: './entrar.page.scss',
})
export class EntrarPage {
  private readonly api = inject(ApiClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.maxLength(EMAIL_MAX)]],
    password: ['', [Validators.required, Validators.maxLength(SENHA_MAX)]],
  });

  protected readonly senhaVisivel = signal(false);
  protected readonly enviando = signal(false);
  protected readonly tentou = signal(false);
  protected readonly erroGeral = signal<string | null>(null);
  /** Segundos que faltam para poder tentar de novo, ou `null` se não há bloqueio. */
  protected readonly bloqueioRestante = signal<number | null>(null);
  protected readonly emailMax = EMAIL_MAX;
  protected readonly senhaMax = SENHA_MAX;
  protected readonly formatarTempoRestante = formatarTempoRestante;

  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.pararContagem());
  }

  protected alternarSenha(): void {
    this.senhaVisivel.update((v) => !v);
  }

  protected obrigatorio(campo: 'email' | 'password'): boolean {
    const control = this.form.controls[campo];
    return (this.tentou() || control.touched) && control.hasError('required');
  }

  protected enviar(): void {
    this.tentou.set(true);
    this.erroGeral.set(null);
    if (this.form.invalid || this.enviando() || this.bloqueioRestante() !== null) return;

    const { email, password } = this.form.getRawValue();
    this.enviando.set(true);
    this.api.post<SessionResponse>('/auth/login', { email: email.trim(), password }).subscribe({
      next: (session) => {
        this.auth.startSession(session);
        void this.router.navigateByUrl(
          rotaDeRetorno(this.route.snapshot.queryParamMap.get('returnUrl')),
        );
      },
      error: (error: unknown) => {
        this.enviando.set(false);
        const problem = asProblem(error);
        if (problem?.code === 'login_locked' && typeof problem.retryAfterSeconds === 'number') {
          this.iniciarContagem(problem.retryAfterSeconds);
          return;
        }
        this.erroGeral.set(problemMessage(error));
      },
    });
  }

  private iniciarContagem(segundos: number): void {
    this.pararContagem();
    const fim = Date.now() + segundos * 1000;
    this.bloqueioRestante.set(segundos);
    this.timer = setInterval(() => {
      const restante = Math.ceil((fim - Date.now()) / 1000);
      if (restante <= 0) {
        this.pararContagem();
        this.bloqueioRestante.set(null);
      } else {
        this.bloqueioRestante.set(restante);
      }
    }, 1000);
  }

  private pararContagem(): void {
    if (this.timer !== null) clearInterval(this.timer);
    this.timer = null;
  }
}
