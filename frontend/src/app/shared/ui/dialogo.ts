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
      zCustomClasses: classes,
    }) as unknown as ZardDialogRef<C, R>;
    const comoDesconhecido = ref as unknown as ZardDialogRef<unknown, unknown>;
    this.abertos.add(comoDesconhecido);
    ref.afterClosed().subscribe(() => this.abertos.delete(comoDesconhecido));
    return ref;
  }

  /** Fecha todos os diálogos abertos por este serviço. */
  closeAll(): void {
    for (const ref of [...this.abertos]) ref.close();
  }
}
