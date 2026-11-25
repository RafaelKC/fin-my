import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCalendarDays,
  lucideCalendarRange,
  lucideCheck,
  lucideChevronDown,
  lucideChevronLeft,
  lucideChevronRight,
  lucideChevronUp,
  lucideCircleCheck,
  lucideCopy,
  lucideCreditCard,
  lucideEllipsis,
  lucideEllipsisVertical,
  lucideEye,
  lucideEyeOff,
  lucideHandshake,
  lucideLayoutDashboard,
  lucideListChecks,
  lucideLogOut,
  lucideMonitor,
  lucideMoon,
  lucidePlus,
  lucideRepeat,
  lucideSearch,
  lucideSun,
  lucideTag,
  lucideUser,
  lucideWallet,
  lucideX,
} from '@ng-icons/lucide';

/** Nomes de ícone usados no app (os mesmos de antes) → ícone Lucide do Zard UI. */
const ICONES = {
  add: 'lucidePlus',
  account_balance_wallet: 'lucideWallet',
  calendar_month: 'lucideCalendarDays',
  check: 'lucideCheck',
  check_circle: 'lucideCircleCheck',
  checklist: 'lucideListChecks',
  chevron_left: 'lucideChevronLeft',
  chevron_right: 'lucideChevronRight',
  close: 'lucideX',
  content_copy: 'lucideCopy',
  credit_card: 'lucideCreditCard',
  dark_mode: 'lucideMoon',
  dashboard: 'lucideLayoutDashboard',
  desktop: 'lucideMonitor',
  event_note: 'lucideCalendarRange',
  expand_less: 'lucideChevronUp',
  expand_more: 'lucideChevronDown',
  handshake: 'lucideHandshake',
  light_mode: 'lucideSun',
  logout: 'lucideLogOut',
  more_horiz: 'lucideEllipsis',
  more_vert: 'lucideEllipsisVertical',
  person: 'lucideUser',
  repeat: 'lucideRepeat',
  search: 'lucideSearch',
  sell: 'lucideTag',
  visibility: 'lucideEye',
  visibility_off: 'lucideEyeOff',
} as const;

export type NomeIcone = keyof typeof ICONES;

/**
 * Ícone do app (Lucide, via `ng-icon` como nos componentes Zard). Decorativo por padrão
 * (`aria-hidden`); com `rotulo`, vira imagem com nome acessível. `data-icone` guarda o nome.
 */
@Component({
  selector: 'app-icone',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon],
  viewProviders: [
    provideIcons({
      lucideCalendarDays,
      lucideCalendarRange,
      lucideCheck,
      lucideChevronDown,
      lucideChevronLeft,
      lucideChevronRight,
      lucideChevronUp,
      lucideCircleCheck,
      lucideCopy,
      lucideCreditCard,
      lucideEllipsis,
      lucideEllipsisVertical,
      lucideEye,
      lucideEyeOff,
      lucideHandshake,
      lucideLayoutDashboard,
      lucideListChecks,
      lucideLogOut,
      lucideMonitor,
      lucideMoon,
      lucidePlus,
      lucideRepeat,
      lucideSearch,
      lucideSun,
      lucideTag,
      lucideUser,
      lucideWallet,
      lucideX,
    }),
  ],
  host: {
    '[attr.data-icone]': 'nome()',
    '[attr.role]': "rotulo() ? 'img' : null",
    '[attr.aria-label]': 'rotulo() || null',
    '[attr.aria-hidden]': "rotulo() ? null : 'true'",
    class: 'inline-flex shrink-0 items-center justify-center',
  },
  template: `<ng-icon [name]="lucide()" [size]="tamanho()" />`,
})
export class Icone {
  readonly nome = input.required<NomeIcone>();
  readonly rotulo = input<string>('');
  readonly tamanho = input<string>('1.25rem');
  protected readonly lucide = computed(() => ICONES[this.nome()]);
}
