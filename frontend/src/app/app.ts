import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterOutlet } from '@angular/router';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { filter } from 'rxjs/operators';
import { ZardSonnerComponent } from '@/shared/components/sonner';
import { ZardDarkMode } from '@/shared/services/dark-mode';
import { Avisos } from '@/shared/ui/avisos';
import { Icone } from '@/shared/ui/icone';
import { WidgetBridgeService } from './core/native/widget-bridge.service';

/** Cor da barra do sistema (PWA) em cada tema; a mesma do fundo do app. */
const COR_TEMA = { light: '#f9f9fb', dark: '#151519' } as const;

@Component({
  imports: [RouterOutlet, ZardSonnerComponent, Icone],
  selector: 'app-root',
  templateUrl: './app.html',
})
export class App {
  /** Tema claro/escuro/sistema (Zard); a escolha fica no aparelho. */
  protected readonly tema = inject(ZardDarkMode);

  /** Sem internet: a faixa no topo avisa que os dados não atualizam (nada fica em cache). */
  protected readonly offline = signal(false);

  private readonly documento = inject(DOCUMENT);
  /** Dentro do app Android, conecta o widget à sessão (010); no navegador não faz nada. */
  private readonly widget = inject(WidgetBridgeService);

  constructor() {
    effect(() => {
      const meta = this.documento.querySelector('meta[name="theme-color"]');
      meta?.setAttribute('content', COR_TEMA[this.tema.themeMode()]);
    });

    const janela = this.documento.defaultView;
    if (janela && typeof janela.addEventListener === 'function') {
      this.offline.set(janela.navigator?.onLine === false);
      const online = () => this.offline.set(false);
      const desligado = () => this.offline.set(true);
      janela.addEventListener('online', online);
      janela.addEventListener('offline', desligado);
      inject(DestroyRef).onDestroy(() => {
        janela.removeEventListener('online', online);
        janela.removeEventListener('offline', desligado);
      });
    }

    // Nova versão do app baixada pelo service worker: oferece recarregar.
    const sw = inject(SwUpdate);
    const avisos = inject(Avisos);
    if (sw.isEnabled) {
      sw.versionUpdates
        .pipe(
          filter((e): e is VersionReadyEvent => e.type === 'VERSION_READY'),
          takeUntilDestroyed(),
        )
        .subscribe(() =>
          avisos
            .open('Nova versão do Fin-My disponível.', 'Atualizar', { duration: Infinity })
            .onAction()
            .subscribe(() => janela?.location.reload()),
        );
    }
  }
}
