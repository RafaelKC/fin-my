import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { ApiClient } from '../../core/api/api-client';
import { AuthService } from '../../core/auth/auth.service';
import { TenantDto, UserDto } from '../../core/auth/session';
import { asProblem, problemMessage } from '../../core/errors/problem-details';
import { AplicativosConectados } from './aplicativos-conectados';

export const NOME_MAX = 60;
export const SENHA_MIN = 8;
export const SENHA_MAX = 128;

type CampoSenha = 'currentPassword' | 'newPassword';

/** Tela `/perfil` (US5, contracts/ui-rotas.md): nome, nome do Espaço, trocar senha e sair. */
@Component({
  selector: 'app-perfil-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatIconModule, AplicativosConectados],
  templateUrl: './perfil.page.html',
  styleUrl: './perfil.page.scss',
})
export class PerfilPage {
  private readonly api = inject(ApiClient);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder).nonNullable;

  protected readonly email = this.auth.user()?.email ?? '';
  protected readonly nomeMax = NOME_MAX;
  protected readonly senhaMin = SENHA_MIN;
  protected readonly senhaMax = SENHA_MAX;

  protected readonly formNome = this.fb.group({
    name: [this.auth.user()?.name ?? '', [Validators.required, Validators.maxLength(NOME_MAX)]],
  });
  protected readonly formEspaco = this.fb.group({
    name: [this.auth.tenant()?.name ?? '', [Validators.required, Validators.maxLength(NOME_MAX)]],
  });
  protected readonly formSenha = this.fb.group({
    currentPassword: ['', [Validators.required]],
    newPassword: ['', [Validators.required, Validators.minLength(SENHA_MIN)]],
  });

  protected readonly tentouNome = signal(false);
  protected readonly salvandoNome = signal(false);
  protected readonly nomeSalvo = signal(false);
  protected readonly erroNome = signal<string | null>(null);

  protected readonly tentouEspaco = signal(false);
  protected readonly salvandoEspaco = signal(false);
  protected readonly espacoSalvo = signal(false);
  protected readonly erroEspaco = signal<string | null>(null);

  protected readonly tentouSenha = signal(false);
  protected readonly trocandoSenha = signal(false);
  protected readonly senhaTrocada = signal(false);
  protected readonly senhaVisivel = signal(false);
  protected readonly erroSenhaGeral = signal<string | null>(null);
  protected readonly errosSenhaServidor = signal<Partial<Record<CampoSenha, string>>>({});

  protected readonly saindo = signal(false);

  protected alternarSenha(): void {
    this.senhaVisivel.update((v) => !v);
  }

  protected erroNomeCampo(form: 'nome' | 'espaco'): string | null {
    const [grupo, tentou, rotulo] =
      form === 'nome'
        ? [this.formNome, this.tentouNome(), 'O nome']
        : [this.formEspaco, this.tentouEspaco(), 'O nome do Espaço'];
    const control = grupo.controls.name;
    if (!(tentou || control.touched) || !control.errors) return null;
    return control.errors['required']
      ? `Informe ${form === 'nome' ? 'seu nome' : 'o nome do Espaço'}.`
      : `${rotulo} pode ter até ${NOME_MAX} caracteres.`;
  }

  protected erroSenha(campo: CampoSenha): string | null {
    const control = this.formSenha.controls[campo];
    if ((this.tentouSenha() || control.touched) && control.errors) {
      if (control.errors['required'])
        return campo === 'currentPassword' ? 'Informe sua senha atual.' : 'Informe a nova senha.';
      return `A senha precisa ter pelo menos ${SENHA_MIN} caracteres.`;
    }
    return this.errosSenhaServidor()[campo] ?? null;
  }

  protected salvarNome(): void {
    this.tentouNome.set(true);
    this.erroNome.set(null);
    this.nomeSalvo.set(false);
    if (this.formNome.invalid || this.salvandoNome()) return;

    this.salvandoNome.set(true);
    this.api
      .patch<{ user: UserDto; tenant: TenantDto }>('/me', {
        name: this.formNome.getRawValue().name.trim(),
      })
      .subscribe({
        next: ({ user }) => {
          this.salvandoNome.set(false);
          this.auth.updateProfile({ user });
          this.formNome.reset({ name: user.name });
          this.tentouNome.set(false);
          this.nomeSalvo.set(true);
        },
        error: (error: unknown) => {
          this.salvandoNome.set(false);
          this.erroNome.set(mensagemDeCampo(error, 'name'));
        },
      });
  }

  protected salvarEspaco(): void {
    this.tentouEspaco.set(true);
    this.erroEspaco.set(null);
    this.espacoSalvo.set(false);
    if (this.formEspaco.invalid || this.salvandoEspaco()) return;

    this.salvandoEspaco.set(true);
    this.api
      .patch<TenantDto>('/tenant', { name: this.formEspaco.getRawValue().name.trim() })
      .subscribe({
        next: (tenant) => {
          this.salvandoEspaco.set(false);
          this.auth.updateProfile({ tenant });
          this.formEspaco.reset({ name: tenant.name });
          this.tentouEspaco.set(false);
          this.espacoSalvo.set(true);
        },
        error: (error: unknown) => {
          this.salvandoEspaco.set(false);
          this.erroEspaco.set(mensagemDeCampo(error, 'name'));
        },
      });
  }

  protected trocarSenha(): void {
    this.tentouSenha.set(true);
    this.erroSenhaGeral.set(null);
    this.errosSenhaServidor.set({});
    this.senhaTrocada.set(false);
    if (this.formSenha.invalid || this.trocandoSenha()) return;

    this.trocandoSenha.set(true);
    this.api.post<void>('/me/password', this.formSenha.getRawValue()).subscribe({
      next: () => {
        this.trocandoSenha.set(false);
        this.formSenha.reset();
        this.tentouSenha.set(false);
        this.senhaTrocada.set(true);
      },
      error: (error: unknown) => {
        this.trocandoSenha.set(false);
        const problem = asProblem(error);
        if (problem?.code === 'current_password_incorrect') {
          this.errosSenhaServidor.set({
            currentPassword: problem.detail || 'A senha atual está incorreta.',
          });
          return;
        }
        const campos: Partial<Record<CampoSenha, string>> = {};
        for (const [chave, mensagens] of Object.entries(problem?.errors ?? {})) {
          const campo = (chave.charAt(0).toLowerCase() + chave.slice(1)) as CampoSenha;
          if (campo in this.formSenha.controls && mensagens[0]) campos[campo] = mensagens[0];
        }
        if (Object.keys(campos).length > 0) this.errosSenhaServidor.set(campos);
        else this.erroSenhaGeral.set(problemMessage(error));
      },
    });
  }

  protected sair(): void {
    if (this.saindo()) return;
    this.saindo.set(true);
    // `logout()` descarta a sessão local e vai para `/entrar` mesmo se a API falhar.
    this.auth.logout().subscribe({ error: () => undefined });
  }
}

function mensagemDeCampo(error: unknown, campo: string): string {
  const mensagens = asProblem(error)?.errors?.[campo];
  return mensagens?.[0] ?? problemMessage(error);
}
