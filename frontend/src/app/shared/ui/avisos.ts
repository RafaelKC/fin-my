import { Injectable, inject } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { ZardSonnerService } from '@/shared/components/sonner';

/** Opções de um aviso passageiro (toast). */
export interface OpcoesAviso {
  /** Tempo na tela em ms. Sem ele: 4 s sem ação, 8 s com ação. */
  duration?: number;
  /** Aceito por compatibilidade; os avisos aparecem sempre no topo. */
  verticalPosition?: 'top' | 'bottom';
  /** Tom do aviso: neutro (padrão), sucesso ou erro. */
  tom?: 'neutro' | 'sucesso' | 'erro';
}

/** Aviso aberto: `onAction()` emite quando a pessoa toca no botão de ação (ex.: "Desfazer"). */
export interface AvisoRef {
  onAction(): Observable<void>;
  dismiss(): void;
}

/**
 * Avisos passageiros do app (toasts do Zard UI, sobre o ngx-sonner): uma mensagem curta e, se
 * houver, um botão de ação. A ação "Fechar" só dispensa o aviso.
 */
@Injectable({ providedIn: 'root' })
export class Avisos {
  private readonly sonner = inject(ZardSonnerService);

  open(mensagem: string, acao?: string, opcoes: OpcoesAviso = {}): AvisoRef {
    const acao$ = new Subject<void>();
    const comAcao = acao !== undefined && acao !== 'Fechar';
    const duracao = opcoes.duration ?? (comAcao ? 8000 : 4000);
    const config = {
      duration: duracao,
      closeButton: acao === 'Fechar',
      action: comAcao
        ? {
            label: acao,
            onClick: () => {
              acao$.next();
              acao$.complete();
            },
          }
        : undefined,
    };
    const tom = opcoes.tom ?? 'neutro';
    const id =
      tom === 'sucesso'
        ? this.sonner.success(mensagem, config)
        : tom === 'erro'
          ? this.sonner.error(mensagem, config)
          : this.sonner.show(mensagem, config);
    return {
      onAction: () => acao$.asObservable(),
      dismiss: () => this.sonner.dismiss(id),
    };
  }
}
