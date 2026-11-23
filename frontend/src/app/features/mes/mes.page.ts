import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Página "Mês" vazia: placeholder até a feature 002. */
@Component({
  selector: 'app-mes-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Mês</h1>
    <p>Nada por aqui ainda. Em breve você verá os lançamentos do mês.</p>
  `,
})
export class MesPage {}
