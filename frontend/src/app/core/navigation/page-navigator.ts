import { Injectable } from '@angular/core';

/** Navegação de página inteira (fora do roteador do Angular), isolada para os testes poderem substituí-la. */
@Injectable({ providedIn: 'root' })
export class PageNavigator {
  go(url: string): void {
    window.location.assign(url);
  }
}
