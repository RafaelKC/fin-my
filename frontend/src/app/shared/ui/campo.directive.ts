import { Directive, ElementRef, inject } from '@angular/core';
import { inputVariants } from '@/shared/components/input/input.variants';
import { mergeClasses } from '@/shared/utils/merge-classes';

/**
 * Aparência do campo de texto do Zard UI (`inputVariants`) em `<input>`/`<textarea>` comuns.
 *
 * O `input[z-input]` do Zard é também um `ControlValueAccessor` com valor próprio (`model`), o
 * que mudaria como as telas leem e gravam os campos (sinais, `[value]`, `formControlName`).
 * Esta diretiva só aplica as classes; o comportamento continua o do elemento nativo.
 */
@Directive({
  selector: 'input[appCampo], textarea[appCampo]',
  host: { 'data-slot': 'input', '[class]': 'classes' },
})
export class CampoDirective {
  private readonly el = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly classes = mergeClasses(
    inputVariants(),
    this.el.tagName === 'TEXTAREA' ? 'h-auto min-h-24 py-2' : '',
  );
}
