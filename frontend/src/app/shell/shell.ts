import { NavegacaoService } from '../core/navegacao/navegacao.service';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Icone, NomeIcone } from '@/shared/ui/icone';
import { ZardButtonComponent } from '@/shared/components/button';
import { EDarkModes, ZardDarkMode } from '@/shared/services/dark-mode';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';
import { AuthService } from '../core/auth/auth.service';
import { LancamentoLauncher } from '../features/lancamento/lancamento.launcher';

/**
 * Casca do app logado: nome do Espaço no topo; navegação na barra inferior (celular) ou no menu
 * lateral (≥ 1024 px). Uma única `<nav>`; o CSS muda a disposição. Quais telas ficam na barra (até 4, duas de cada lado
 * do "+") e a ordem vêm de [NavegacaoService] (padrão: Mês · Cartões · + · Acertos · Mais); no menu lateral todos aparecem
 * direto, na mesma ordem.
 */
@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icone, ZardButtonComponent],
  host: { '(document:keydown.escape)': 'maisAberto.set(false)' },
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  protected readonly espaco = inject(AuthService).tenant;

  private readonly lancamento = inject(LancamentoLauncher);

  private readonly router = inject(Router);

  /** Barra/menu personalizável (Perfil › Navegação): itens antes e depois do "+" e os do "Mais". */
  protected readonly navegacao = inject(NavegacaoService);

  protected readonly maisAberto = signal(false);

  private readonly tema = inject(ZardDarkMode);

  /** Tema escolhido no aparelho: sistema → claro → escuro → sistema. */
  protected readonly iconeTema = computed<NomeIcone>(() => {
    const atual = this.tema.currentTheme();
    return atual === EDarkModes.LIGHT
      ? 'light_mode'
      : atual === EDarkModes.DARK
        ? 'dark_mode'
        : 'desktop';
  });

  protected readonly rotuloTema = computed(() => {
    const atual = this.tema.currentTheme();
    const nome =
      atual === EDarkModes.LIGHT ? 'claro' : atual === EDarkModes.DARK ? 'escuro' : 'do sistema';
    return `Tema ${nome}. Trocar tema`;
  });

  protected alternarTema(): void {
    const atual = this.tema.currentTheme();
    this.tema.toggleTheme(
      atual === EDarkModes.SYSTEM
        ? EDarkModes.LIGHT
        : atual === EDarkModes.LIGHT
          ? EDarkModes.DARK
          : EDarkModes.SYSTEM,
    );
  }

  constructor() {
    // Navegar (por qualquer meio) fecha o painel "Mais".
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((e) => {
        this.maisAberto.set(false);
        this.abrirLancamentoPedidoPeloWidget(e as NavigationEnd);
      });
  }

  /**
   * Widget "Lançar" do app Android (010): abre o app em `?lancar=1`. Abre o formulário rápido e tira o parâmetro da URL
   * (voltar não reabre o formulário).
   */
  private abrirLancamentoPedidoPeloWidget(e: NavigationEnd): void {
    // URL pedida (antes dos redirecionamentos): `/mes?lancar=1` vira `/mes/AAAA-MM` e o parâmetro se perde.
    if (this.router.parseUrl(e.url).queryParams['lancar'] !== '1') return;
    if (this.router.parseUrl(e.urlAfterRedirects).queryParams['lancar'] === '1') {
      void this.router.navigate([], {
        queryParams: { lancar: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
    this.lancamento.abrir();
  }

  protected alternarMais(): void {
    this.maisAberto.update((v) => !v);
  }

  /** "+" global: abre o formulário rápido de qualquer tela logada (FR-008). */
  protected lancar(): void {
    this.lancamento.abrir();
  }
}
