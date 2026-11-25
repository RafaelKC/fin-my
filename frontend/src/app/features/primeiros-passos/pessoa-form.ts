import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { asProblem, problemMessage } from '../../core/errors/problem-details';
import { TAG_NOME_MAX } from '../../shared/tag-picker/tag.models';
import { TagsService } from '../../shared/tag-picker/tags.service';
import { ZardButtonComponent } from '@/shared/components/button';
import { CampoDirective } from '@/shared/ui/campo.directive';

/** Cadastro de uma pessoa que devolve dinheiro: cria uma Tag de acerto (004 FR-001; 006 US3.1). */
@Component({
  imports: [ZardButtonComponent, CampoDirective],
  selector: 'app-pessoa-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="form" (submit)="adicionar(); $event.preventDefault()" novalidate>
      <div class="campo">
        <label for="pessoa-nome">Nome da pessoa</label>
        <input
          appCampo
          id="pessoa-nome"
          type="text"
          autocomplete="off"
          [attr.maxlength]="nomeMax"
          [value]="nome()"
          (input)="nome.set($any($event.target).value)"
        />
        @if (erro(); as mensagem) {
          <span class="erro" role="alert" data-testid="erro-pessoa">{{ mensagem }}</span>
        }
      </div>
      <button z-button type="submit" class="enviar" [disabled]="ocupado()">
        {{ ocupado() ? 'Salvando…' : 'Adicionar pessoa' }}
      </button>
    </form>
  `,
  styles: `
    .form {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
    .campo {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
  `,
})
export class PessoaForm {
  private readonly tags = inject(TagsService);

  readonly salvou = output<string>();

  protected readonly nomeMax = TAG_NOME_MAX;
  protected readonly nome = signal('');
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected adicionar(): void {
    if (this.ocupado()) return;
    const nome = this.nome().trim();
    if (nome === '' || nome.length > TAG_NOME_MAX) {
      this.erro.set(`Informe um nome de até ${TAG_NOME_MAX} caracteres.`);
      return;
    }
    this.erro.set(null);
    this.ocupado.set(true);
    this.tags.create({ name: nome, isSettlement: true }).subscribe({
      next: (tag) => {
        this.ocupado.set(false);
        this.nome.set('');
        this.salvou.emit(tag.name);
      },
      error: (error: unknown) => {
        this.ocupado.set(false);
        this.erro.set(
          asProblem(error)?.code === 'tag.name_taken'
            ? 'Já existe uma tag com esse nome.'
            : problemMessage(error),
        );
      },
    });
  }
}
