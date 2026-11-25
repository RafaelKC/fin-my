import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  forwardRef,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { formatBrl, parseValueExpression, valueExpressionMessages } from '../../core/money';

/** Tecla do teclado numérico: o que aparece e o que é inserido no texto (a porta TS só aceita `*` e `-`). */
export interface AmountKey {
  readonly label: string;
  readonly insert: string;
  readonly ariaLabel: string;
  readonly wide?: boolean;
}

const key = (
  label: string,
  ariaLabel: string,
  insert: string = label,
  wide = false,
): AmountKey => ({
  label,
  insert,
  ariaLabel,
  wide,
});

/** Teclas em grade de 4 colunas: `0-9 , + − × ÷ ( )` (⌫ é tratada à parte). */
export const AMOUNT_KEYS: readonly AmountKey[] = [
  key('7', '7'),
  key('8', '8'),
  key('9', '9'),
  key('÷', 'dividir'),
  key('4', '4'),
  key('5', '5'),
  key('6', '6'),
  key('×', 'multiplicar', '*'),
  key('1', '1'),
  key('2', '2'),
  key('3', '3'),
  key('−', 'subtrair', '-'),
  key(',', 'vírgula'),
  key('0', '0'),
  key('⌫', 'apagar'),
  key('+', 'somar'),
  key('(', 'abrir parênteses', '(', true),
  key(')', 'fechar parênteses', ')', true),
];

const BACKSPACE = '⌫';
const MAX_LENGTH = 200;

/**
 * Campo de valor (RT-05): texto livre com calculadora e pré-visualização "= R$ 102,20" ou a
 * mensagem de erro do parser. Em `pointer: coarse` usa teclado numérico próprio (sem abrir o do
 * sistema); no computador é um campo de texto comum. O valor do formulário é o texto digitado.
 */
@Component({
  selector: 'app-amount-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './amount-input.html',
  styleUrl: './amount-input.scss',
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => AmountInput), multi: true },
  ],
})
export class AmountInput implements ControlValueAccessor {
  readonly label = input('Valor');
  /** Aceita zero e negativo (ex.: saldo inicial). */
  readonly allowNonPositive = input(false);

  private readonly field = viewChild<ElementRef<HTMLInputElement>>('field');

  protected readonly keys = AMOUNT_KEYS;
  protected readonly maxLength = MAX_LENGTH;
  protected readonly text = signal('');
  protected readonly disabled = signal(false);
  protected readonly keypad = signal(
    typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches,
  );

  protected readonly preview = computed(() => {
    const text = this.text();
    if (text.trim().length === 0) {
      return null;
    }

    const result = parseValueExpression(text, this.allowNonPositive());
    return result.ok
      ? { ok: true, text: `= ${formatBrl(result.cents)}` }
      : { ok: false, text: valueExpressionMessages[result.error] };
  });

  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  /** Foca o campo (no celular o teclado do sistema não abre: o campo é só leitura). */
  focus(): void {
    this.field()?.nativeElement.focus();
  }

  writeValue(value: string | null | undefined): void {
    this.text.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  protected onInput(event: Event): void {
    this.update((event.target as HTMLInputElement).value);
  }

  protected press(k: AmountKey): void {
    this.update(this.text() + k.insert);
  }

  protected backspace(): void {
    this.update(this.text().slice(0, -1));
  }

  protected touch(): void {
    this.onTouched();
  }

  protected isBackspace(k: AmountKey): boolean {
    return k.label === BACKSPACE;
  }

  /** Operadores e parênteses ganham outra cor no teclado. */
  protected ehOperador(k: AmountKey): boolean {
    return !/^[0-9,]$/.test(k.label) && !this.isBackspace(k);
  }

  private update(value: string): void {
    if (this.disabled() || value.length > MAX_LENGTH) {
      return;
    }

    this.text.set(value);
    this.onChange(value);
    this.onTouched();
  }
}
