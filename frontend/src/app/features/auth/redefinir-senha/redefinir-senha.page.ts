import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiClient } from '../../../core/api/api-client';
import { asProblem, problemMessage } from '../../../core/errors/problem-details';

export const SENHA_MIN = 8;
export const SENHA_MAX = 128;

/** Tela `/redefinir-senha?token=` (US4, contracts/ui-rotas.md): define a nova senha. */
@Component({
  selector: 'app-redefinir-senha-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, MatIconModule],
  templateUrl: './redefinir-senha.page.html',
  styleUrl: './redefinir-senha.page.scss',
})
export class RedefinirSenhaPage {
  private readonly api = inject(ApiClient);
  private readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token')?.trim() ?? '';

  protected readonly form = inject(FormBuilder).nonNullable.group({
    password: ['', [Validators.required, Validators.minLength(SENHA_MIN)]],
  });

  protected readonly senhaVisivel = signal(false);
  protected readonly enviando = signal(false);
  protected readonly tentou = signal(false);
  protected readonly concluido = signal(false);
  /** Sem token na URL ou 410 `reset_token_invalid` (expirado, usado ou inexistente). */
  protected readonly tokenInvalido = signal(this.token === '');
  protected readonly erroGeral = signal<string | null>(null);
  protected readonly erroDoServidor = signal<string | null>(null);

  protected readonly senhaMin = SENHA_MIN;
  protected readonly senhaMax = SENHA_MAX;

  protected alternarSenha(): void {
    this.senhaVisivel.update((v) => !v);
  }

  protected erro(): string | null {
    const control = this.form.controls.password;
    if ((this.tentou() || control.touched) && control.errors) {
      return control.errors['required']
        ? 'Informe uma senha.'
        : `A senha precisa ter pelo menos ${SENHA_MIN} caracteres.`;
    }
    return this.erroDoServidor();
  }

  protected enviar(): void {
    this.tentou.set(true);
    this.erroGeral.set(null);
    this.erroDoServidor.set(null);
    if (this.form.invalid || this.enviando()) return;

    this.enviando.set(true);
    this.api
      .post<void>('/auth/password-reset/confirm', {
        token: this.token,
        newPassword: this.form.getRawValue().password,
      })
      .subscribe({
        next: () => {
          this.enviando.set(false);
          this.concluido.set(true);
        },
        error: (error: unknown) => {
          this.enviando.set(false);
          this.tratarErro(error);
        },
      });
  }

  private tratarErro(error: unknown): void {
    const problem = asProblem(error);
    if (problem?.code === 'reset_token_invalid') {
      this.tokenInvalido.set(true);
      return;
    }
    const mensagem = problem?.errors?.['newPassword']?.[0] ?? problem?.errors?.['password']?.[0];
    if (mensagem) {
      this.erroDoServidor.set(mensagem);
      return;
    }
    this.erroGeral.set(problemMessage(error));
  }
}
