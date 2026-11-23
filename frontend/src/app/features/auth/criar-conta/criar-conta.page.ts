import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { Router, RouterLink } from '@angular/router';
import { ApiClient } from '../../../core/api/api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { SessionResponse } from '../../../core/auth/session';
import { asProblem, problemMessage } from '../../../core/errors/problem-details';

export const SENHA_MIN = 8;
export const SENHA_MAX = 128;
export const NOME_MAX = 60;
export const EMAIL_MAX = 254;

type Campo = 'name' | 'email' | 'password';

/** Tela `/criar-conta` (US1, contracts/ui-rotas.md): cria Usuário + Espaço e já entra. */
@Component({
  selector: 'app-criar-conta-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, MatIconModule],
  templateUrl: './criar-conta.page.html',
  styleUrl: './criar-conta.page.scss',
})
export class CriarContaPage {
  private readonly api = inject(ApiClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(NOME_MAX)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(EMAIL_MAX)]],
    password: ['', [Validators.required, Validators.minLength(SENHA_MIN)]],
  });

  protected readonly senhaVisivel = signal(false);
  protected readonly enviando = signal(false);
  protected readonly tentou = signal(false);
  protected readonly emailJaCadastrado = signal(false);
  protected readonly erroGeral = signal<string | null>(null);
  /** Mensagens por campo vindas da API (422 `validation_failed`). */
  protected readonly errosDoServidor = signal<Partial<Record<Campo, string>>>({});

  protected readonly senhaMin = SENHA_MIN;
  protected readonly senhaMax = SENHA_MAX;
  protected readonly nomeMax = NOME_MAX;

  protected alternarSenha(): void {
    this.senhaVisivel.update((v) => !v);
  }

  /** Mensagem do campo (cliente primeiro, depois a do servidor), ou `null` se estiver ok. */
  protected erro(campo: Campo): string | null {
    const control = this.form.controls[campo];
    if (this.tentou() || control.touched) {
      const e = control.errors;
      if (e) {
        if (campo === 'name')
          return e['required']
            ? 'Informe seu nome.'
            : `O nome pode ter até ${NOME_MAX} caracteres.`;
        if (campo === 'email')
          return e['required']
            ? 'Informe seu e-mail.'
            : e['email']
              ? 'Informe um e-mail válido.'
              : 'O e-mail é grande demais.';
        return e['required']
          ? 'Informe uma senha.'
          : `A senha precisa ter pelo menos ${SENHA_MIN} caracteres.`;
      }
    }
    return this.errosDoServidor()[campo] ?? null;
  }

  protected enviar(): void {
    this.tentou.set(true);
    this.erroGeral.set(null);
    this.emailJaCadastrado.set(false);
    this.errosDoServidor.set({});
    if (this.form.invalid || this.enviando()) return;

    const { name, email, password } = this.form.getRawValue();
    this.enviando.set(true);
    this.api
      .post<SessionResponse>('/auth/signup', { name: name.trim(), email: email.trim(), password })
      .subscribe({
        next: (session) => {
          this.auth.startSession(session);
          void this.router.navigateByUrl('/');
        },
        error: (error: unknown) => {
          this.enviando.set(false);
          this.tratarErro(error);
        },
      });
  }

  private tratarErro(error: unknown): void {
    const problem = asProblem(error);
    if (problem?.code === 'email_already_registered') {
      this.emailJaCadastrado.set(true);
      return;
    }
    if (error instanceof HttpErrorResponse && problem?.errors) {
      const campos: Partial<Record<Campo, string>> = {};
      for (const [chave, mensagens] of Object.entries(problem.errors)) {
        const campo = chave.toLowerCase() as Campo;
        if (campo in this.form.controls && mensagens[0]) campos[campo] = mensagens[0];
      }
      if (Object.keys(campos).length > 0) {
        this.errosDoServidor.set(campos);
        return;
      }
    }
    this.erroGeral.set(problemMessage(error));
  }
}
