import { Injectable, inject } from '@angular/core';
import { CardPurchase } from './card-purchase.models';
import { Entry } from './entry.models';
import { EntradaPreenchida, LancamentoSheet, LancamentoSheetData } from './lancamento.sheet';
import { Dialogo, DialogoRef } from '@/shared/ui/dialogo';

/** Largura a partir da qual o formulário é diálogo (abaixo disso, tela cheia). */
const DESKTOP = '(min-width: 1024px)';

/** Abre o formulário rápido: tela cheia no celular, diálogo no computador (Zard dialog). */
@Injectable({ providedIn: 'root' })
export class LancamentoLauncher {
  private readonly dialog = inject(Dialogo);
  private aberto: DialogoRef<LancamentoSheet, Entry | undefined> | null = null;

  /** Sem `entry`, abre para lançar; com `entry`, abre para editar esse lançamento. */
  abrir(entry?: Entry): DialogoRef<LancamentoSheet, Entry | undefined> {
    return this.abrirCom(entry ? { entry } : {});
  }

  /** Abre o formulário editando uma compra no cartão (003 FR-015). */
  abrirCompra(purchase: CardPurchase): DialogoRef<LancamentoSheet, Entry | undefined> {
    return this.abrirCom({ purchase });
  }

  /** Abre uma Entrada nova já preenchida, para o "Gerar cobrança" dos acertos (004 R4). */
  abrirPreenchida(preencher: EntradaPreenchida): DialogoRef<LancamentoSheet, Entry | undefined> {
    return this.abrirCom({ preencher });
  }

  private abrirCom(data: LancamentoSheetData): DialogoRef<LancamentoSheet, Entry | undefined> {
    if (this.aberto !== null) return this.aberto;

    const desktop = typeof matchMedia === 'function' && matchMedia(DESKTOP).matches;
    const ref = this.dialog.open<LancamentoSheet, LancamentoSheetData, Entry | undefined>(
      LancamentoSheet,
      desktop ? { data, width: '32rem', classes: 'dialogo-formulario' } : { data, telaCheia: true },
    );
    this.aberto = ref;
    ref.afterClosed().subscribe(() => (this.aberto = null));
    return ref;
  }
}
