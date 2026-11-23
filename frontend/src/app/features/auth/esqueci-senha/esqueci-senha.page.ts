import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiClient } from '../../../core/api/api-client';
import { problemMessage } from '../../../core/errors/problem-details';

export const EMAIL_MAX = 254;

/** Mensagem única de confirmação (FR-011): igual exista ou não conta com o e-mail. */
export const MENSAGEM_LINK_ENVIADO = 'Se houver conta com este e-mail, enviaremos um link.';

/** Tela `/esqueci-senha` (US4, contracts/ui-rotas.md): pede o link de redefinição por e-mail. */
@Component({
  selector: 'app-esqueci-senha-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './esqueci-senha.page.html',
  styleUrl: './esqueci-senha.page.scss',
})
export class EsqueciSenhaPage {
  private readonly api = inject(ApiClient);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(EMAIL_MAX)]],
  });

  protected readonly enviando = signal(false);
  protected readonly tentou = signal(false);
  protected readonly enviado = signal(false);
  protected readonly erroGeral = signal<string | null>(null);
  protected readonly mensagemConfirmacao = MENSAGEM_LINK_ENVIADO;

  protected erro(): string | null {
    const control = this.form.controls.email;
    if (!(this.tentou() || control.touched) || !control.errors) return null;
    return control.errors['required']
      ? 'Informe seu e-mail.'
      : control.errors['email']
        ? 'Informe um e-mail válido.'
        : 'O e-mail é grande demais.';
  }

  protected enviar(): void {
    this.tentou.set(true);
    this.erroGeral.set(null);
    if (this.form.invalid || this.enviando()) return;

    this.enviando.set(true);
    this.api
      .post<void>('/auth/password-reset/request', { email: this.form.getRawValue().email.trim() })
      .subscribe({
        next: () => {
          this.enviando.set(false);
          this.enviado.set(true);
        },
        error: (error: unknown) => {
          this.enviando.set(false);
          this.erroGeral.set(problemMessage(error));
        },
      });
  }
}
