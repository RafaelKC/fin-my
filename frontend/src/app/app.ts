import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ZardSonnerComponent } from '@/shared/components/sonner';
import { ZardDarkMode } from '@/shared/services/dark-mode';

@Component({
  imports: [RouterOutlet, ZardSonnerComponent],
  selector: 'app-root',
  templateUrl: './app.html',
})
export class App {
  /** Tema claro/escuro/sistema (Zard); a escolha fica no aparelho. */
  protected readonly tema = inject(ZardDarkMode);
}
