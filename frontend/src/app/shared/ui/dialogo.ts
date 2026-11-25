import { Injectable, Type, inject } from '@angular/core';
import type { ClassValue } from 'clsx';
import { ZardDialogRef, ZardDialogService } from '@/shared/components/dialog';

export {
  Z_MODAL_DATA as DADOS_DIALOGO,
  ZardDialogRef as DialogoRef,
} from '@/shared/components/dialog';

/** Opções de abertura de um diálogo do app (subconjunto do que as telas usam). */
export interface OpcoesDialogo<D> {
  data?: D;
  /** Largura explícita (ex.: `32rem`); sem ela, a largura responsiva do Zard (até `sm`). */
  width?: string;
  /** Formulário rápido no celular: ocupa a tela inteira. */
  telaCheia?: boolean;
  /** Classes extras do painel. */
  classes?: ClassValue;
  /** Toque fora fecha (padrão: sim, como o Esc). */
  fecharFora?: boolean;
}

/**
 * Abre diálogos do app com o `ZardDialogService` (Zard UI): sem rodapé nem botão de fechar
 * automáticos, porque cada conteúdo traz os próprios botões. O conteúdo recebe os dados por
 * `inject(DADOS_DIALOGO)` e fecha com `inject(DialogoRef).close(resultado)`; quem abriu lê o
 * resultado em `afterClosed()` (`undefined` quando fecha por Esc ou toque fora).
 */
@Injectable({ providedIn: 'root' })
export class Dialogo {
  private readonly zard = inject(ZardDialogService);
  private readonly abertos = new Set<ZardDialogRef<unknown, unknown>>();

  open<C, D = unknown, R = unknown>(
    componente: Type<C>,
    opcoes: OpcoesDialogo<D> = {},
  ): ZardDialogRef<C, R> {
    const classes: ClassValue[] = [];
    if (opcoes.telaCheia) classes.push('dialogo-tela-cheia');
    classes.push(opcoes.width ? 'sm:max-w-none' : 'sm:max-w-md');
    if (opcoes.classes) classes.push(opcoes.classes);
    const ref = this.zard.create<C, D>({
      zContent: componente,
      zData: opcoes.data,
      zHideFooter: true,
      zClosable: false,
      zMaskClosable: opcoes.fecharFora ?? true,
      zWidth: opcoes.telaCheia ? undefined : opcoes.width,
      // Tela cheia: sem a transição de escala do Zard (a folha já nasce do tamanho da tela).
      zDuration: opcoes.telaCheia ? 0 : undefined,
      zCustomClasses: classes,
    }) as unknown as ZardDialogRef<C, R>;
    const comoDesconhecido = ref as unknown as ZardDialogRef<unknown, unknown>;
    this.abertos.add(comoDesconhecido);
    this.esconderFundo();
    ref.afterClosed().subscribe(() => {
      this.abertos.delete(comoDesconhecido);
      if (this.abertos.size === 0) this.mostrarFundo();
    });
    return ref;
  }

  /**
   * Diálogo modal: o resto da página sai da árvore de acessibilidade enquanto ele está aberto
   * (como o MatDialog fazia), para leitores de tela e buscas por papel não verem o fundo.
   * Os avisos (toasts) continuam visíveis.
   */
  private escondidos: Element[] = [];

  private esconderFundo(): void {
    if (this.escondidos.length > 0 || typeof document === 'undefined') return;
    const raiz = document.querySelector('app-root');
    const candidatos = [
      ...Array.from(document.body.children).filter(
        (e) =>
          e !== raiz && !e.classList.contains('cdk-overlay-container') && e.tagName !== 'SCRIPT',
      ),
      ...Array.from(raiz?.children ?? []).filter((e) => e.tagName !== 'Z-SONNER'),
    ];
    for (const e of candidatos) {
      if (e.getAttribute('aria-hidden') === 'true') continue;
      e.setAttribute('aria-hidden', 'true');
      this.escondidos.push(e);
    }
  }

  private mostrarFundo(): void {
    for (const e of this.escondidos) e.removeAttribute('aria-hidden');
    this.escondidos = [];
  }

  /** Fecha todos os diálogos abertos por este serviço. */
  closeAll(): void {
    for (const ref of [...this.abertos]) ref.close();
  }
}
