import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { bancoPorCodigo, logoDoBanco } from './bancos';

/** Logo do banco (círculo branco com o SVG da marca); nada quando não há banco. */
@Component({
  selector: 'app-banco-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (banco(); as b) {
      <span
        class="logo"
        [style.width.px]="tamanho()"
        [style.height.px]="tamanho()"
        aria-hidden="true"
      >
        <img [src]="src(b.codigo)" alt="" loading="lazy" />
      </span>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
    }
    .logo {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      overflow: hidden;
      border-radius: 999px;
      background: #fff;
      box-shadow: 0 0 0 1px color-mix(in oklch, var(--foreground) 12%, transparent);
    }
    img {
      width: 78%;
      height: 78%;
      object-fit: contain;
    }
  `,
})
export class BancoLogo {
  readonly codigo = input<string | null | undefined>(null);
  readonly tamanho = input(28);
  protected readonly banco = computed(() => bancoPorCodigo(this.codigo()));
  protected readonly src = logoDoBanco;
}
