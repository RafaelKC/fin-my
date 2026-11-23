import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { AmountInput } from './amount-input';

@Component({
  imports: [ReactiveFormsModule, AmountInput],
  template: `<app-amount-input [formControl]="control" [allowNonPositive]="nonPositive()" />`,
})
class HostComponent {
  control = new FormControl<string>('', { nonNullable: true });
  nonPositive = signal(false);
}

function setup(coarse: boolean): {
  fixture: ComponentFixture<HostComponent>;
  host: HostComponent;
  el: HTMLElement;
} {
  window.matchMedia = ((q: string) =>
    ({ matches: coarse && q.includes('coarse'), media: q }) as MediaQueryList) as typeof matchMedia;
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  return { fixture, host: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

const press = (el: HTMLElement, name: string): void =>
  (el.querySelector(`button[aria-label="${name}"]`) as HTMLButtonElement).click();
const preview = (el: HTMLElement): string =>
  (el.querySelector('.amount__preview') as HTMLElement).textContent!.trim();

describe('AmountInput', () => {
  const original = window.matchMedia;
  afterEach(() => (window.matchMedia = original));

  describe('computador (pointer: fine)', () => {
    it('não mostra o teclado e aceita digitação no campo', () => {
      const { fixture, host, el } = setup(false);
      expect(el.querySelector('.keypad')).toBeNull();

      const input = el.querySelector('input') as HTMLInputElement;
      input.value = '12,50+3';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(host.control.value).toBe('12,50+3');
      expect(preview(el)).toBe('= R$ 15,50');
      expect(input.readOnly).toBe(false);
    });

    it('mostra a mensagem de erro do parser e some quando vazio', () => {
      const { fixture, host, el } = setup(false);
      const input = el.querySelector('input') as HTMLInputElement;

      input.value = '10/0';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(preview(el)).toBe('Divisão por zero');
      expect(input.getAttribute('aria-invalid')).toBe('true');

      input.value = 'abc';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(preview(el)).toContain('Valor inválido');

      input.value = '';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(preview(el)).toBe('');
      expect(host.control.value).toBe('');
    });

    it('rejeita zero por padrão e aceita com allowNonPositive', () => {
      const { fixture, host, el } = setup(false);
      host.control.setValue('0');
      fixture.detectChanges();
      expect(preview(el)).toBe('Informe um valor maior que zero');

      host.nonPositive.set(true);
      fixture.detectChanges();
      expect(preview(el)).toBe('= R$ 0,00');
    });
  });

  describe('celular (pointer: coarse)', () => {
    it('mostra as 18 teclas com alvo mínimo de 44 px declarado', () => {
      const { el } = setup(true);
      const buttons = el.querySelectorAll('.keypad button');
      expect(buttons.length).toBe(18);
      const labels = Array.from(buttons).map((b) => b.textContent!.trim());
      expect(labels).toEqual(expect.arrayContaining([...'0123456789,+−×÷()⌫'].map((c) => c)));
      expect((el.querySelector('input') as HTMLInputElement).readOnly).toBe(true);
      expect(el.querySelector('input')!.getAttribute('inputmode')).toBe('none');
    });

    it('monta a expressão com × e − mapeados para * e - e calcula a pré-visualização', () => {
      const { fixture, host, el } = setup(true);
      for (const k of [
        'abrir parênteses',
        '3',
        'somar',
        '2',
        'fechar parênteses',
        'multiplicar',
        '2',
        'vírgula',
        '5',
      ]) {
        press(el, k);
      }
      fixture.detectChanges();
      expect(host.control.value).toBe('(3+2)*2,5');
      expect(preview(el)).toBe('= R$ 12,50');

      press(el, 'subtrair');
      press(el, '2');
      press(el, 'dividir');
      press(el, '4');
      fixture.detectChanges();
      expect(host.control.value).toBe('(3+2)*2,5-2÷4');
      expect(preview(el)).toBe('= R$ 12,00');
    });

    it('⌫ apaga o último caractere', () => {
      const { fixture, host, el } = setup(true);
      press(el, '1');
      press(el, '2');
      press(el, 'apagar');
      fixture.detectChanges();
      expect(host.control.value).toBe('1');
      press(el, 'apagar');
      press(el, 'apagar');
      expect(host.control.value).toBe('');
    });

    it('marca como tocado e respeita desabilitado', () => {
      const { fixture, host, el } = setup(true);
      expect(host.control.touched).toBe(false);
      press(el, '5');
      expect(host.control.touched).toBe(true);

      host.control.disable();
      fixture.detectChanges();
      expect((el.querySelector('input') as HTMLInputElement).disabled).toBe(true);
      expect((el.querySelector('.keypad button') as HTMLButtonElement).disabled).toBe(true);
    });
  });

  it('reflete valor definido pelo formulário (writeValue)', () => {
    const { fixture, host, el } = setup(false);
    host.control.setValue('100+2,20');
    fixture.detectChanges();
    expect((el.querySelector('input') as HTMLInputElement).value).toBe('100+2,20');
    expect(preview(el)).toBe('= R$ 102,20');
  });
});
