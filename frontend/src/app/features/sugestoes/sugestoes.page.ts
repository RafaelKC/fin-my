import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { problemMessage } from '../../core/errors/problem-details';
import { Icone } from '@/shared/ui/icone';
import { BancoLogo } from '../../shared/bancos/banco-logo';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { Sugestao, SugestoesService } from './sugestoes.service';

/** Data local AAAA-MM-DD de um instante ISO. */
function dataLocal(iso: string): string {
  const d = new Date(iso);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/**
 * Sugestões de lançamento lidas das notificações de banco no celular (010): Lançar abre o formulário rápido
 * pré-preenchido (tipo, valor, descrição, data, cartão/carteira do mesmo banco); Descartar tira da lista.
 * `/sugestoes/:id` (toque na notificação) abre direto o formulário daquela sugestão.
 */
@Component({
  selector: 'app-sugestoes-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icone, BancoLogo],
  templateUrl: './sugestoes.page.html',
  styleUrl: './sugestoes.page.scss',
})
export class SugestoesPage {
  private readonly servico = inject(SugestoesService);
  private readonly lancamento = inject(LancamentoLauncher);
  private readonly router = inject(Router);

  protected readonly itens = signal<Sugestao[]>([]);
  protected readonly carregando = signal(true);
  protected readonly erro = signal<string | null>(null);

  constructor() {
    const id = inject(ActivatedRoute).snapshot.paramMap.get('id');
    this.carregar(id);
  }

  protected rotuloData(iso: string): string {
    const d = new Date(iso);
    const p = (n: number) => String(n).padStart(2, '0');
    return `${p(d.getDate())}/${p(d.getMonth() + 1)} ${p(d.getHours())}:${p(d.getMinutes())}`;
  }

  protected lancar(s: Sugestao): void {
    const date = dataLocal(s.occurredAt);
    this.lancamento.abrirPreenchida({
      kind: s.kind === 'card_purchase' ? 'card' : s.kind,
      description: s.description,
      amountCents: s.amountCents,
      date,
      referenceMonth: date.slice(0, 7),
      tagIds: [],
      cardId: s.suggestedCreditCardId,
      walletId: s.suggestedWalletId,
      aoSalvar: (linkedId) =>
        this.servico.lancada(s.id, linkedId).subscribe({ next: () => this.remover(s.id) }),
    });
  }

  protected descartar(s: Sugestao): void {
    this.servico.descartar(s.id).subscribe({
      next: () => this.remover(s.id),
      error: (error: unknown) => this.erro.set(problemMessage(error)),
    });
  }

  private remover(id: string): void {
    this.itens.update((lista) => lista.filter((s) => s.id !== id));
  }

  private carregar(abrirId: string | null): void {
    this.servico.listar().subscribe({
      next: (lista) => {
        this.itens.set(lista);
        this.carregando.set(false);
        const alvo = abrirId ? lista.find((s) => s.id === abrirId) : undefined;
        if (abrirId) void this.router.navigate(['/sugestoes'], { replaceUrl: true });
        if (alvo) this.lancar(alvo);
        else if (abrirId)
          this.erro.set(
            'Essa sugestão não está aqui: já foi lançada ou descartada, ou é de outra conta.',
          );
      },
      error: (error: unknown) => {
        this.carregando.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }
}
