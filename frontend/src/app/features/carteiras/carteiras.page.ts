import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icone } from '@/shared/ui/icone';
import { ZardButtonComponent } from '@/shared/components/button';
import { BrlPipe } from '../../core/money';
import { problemMessage } from '../../core/errors/problem-details';
import { Wallet } from './wallet.models';
import { WalletsService } from './wallets.service';

/** Tela `/carteiras` (US1): carteiras com saldo atual e cor, principal marcada, filtro Arquivadas. */
@Component({
  selector: 'app-carteiras-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, BrlPipe, Icone, ZardButtonComponent],
  templateUrl: './carteiras.page.html',
  styleUrl: './carteiras.page.scss',
})
export class CarteirasPage {
  private readonly wallets = inject(WalletsService);

  protected readonly arquivadas = signal(false);
  protected readonly carregando = signal(true);
  protected readonly erro = signal<string | null>(null);
  protected readonly carteiras = signal<Wallet[]>([]);

  constructor() {
    this.carregar();
  }

  protected alternarArquivadas(): void {
    this.arquivadas.update((v) => !v);
    this.carregar();
  }

  private carregar(): void {
    this.carregando.set(true);
    this.erro.set(null);
    this.wallets.list(this.arquivadas() ? 'true' : 'false').subscribe({
      next: (lista) => {
        this.carteiras.set(lista);
        this.carregando.set(false);
      },
      error: (error: unknown) => {
        this.carregando.set(false);
        this.erro.set(problemMessage(error));
      },
    });
  }
}
