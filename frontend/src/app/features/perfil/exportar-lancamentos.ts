import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ApiClient } from '../../core/api/api-client';
import { MENSAGEM_ERRO_GENERICO, problemMessage } from '../../core/errors/problem-details';
import { today } from '../../core/money';
import { HttpErrorResponse } from '@angular/common/http';

/** Limite do intervalo exportado (research R4, igual ao do servidor). */
export const EXPORTAR_MAX_MESES = 120;

const MES = /^(\d{4})-(\d{2})$/;

/** Quantos meses o intervalo cobre (inclusive), ou `null` se algum mês for inválido. */
export function mesesNoIntervalo(de: string, ate: string): number | null {
  const a = MES.exec(de);
  const b = MES.exec(ate);
  if (!a || !b) return null;
  return (Number(b[1]) - Number(a[1])) * 12 + Number(b[2]) - Number(a[2]) + 1;
}

/** Nome do arquivo no `Content-Disposition` (`filename="…"`); `null` se não houver. */
export function nomeDoArquivo(disposition: string | null): string | null {
  const m = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition ?? '');
  return m ? decodeURIComponent(m[1]) : null;
}

/** Seção "Exportar lançamentos (CSV)" do `/perfil` (006 US4): mês inicial e final, e baixa o arquivo. */
@Component({
  selector: 'app-exportar-lancamentos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './exportar-lancamentos.html',
  styleUrl: './exportar-lancamentos.scss',
})
export class ExportarLancamentos {
  private readonly api = inject(ApiClient);
  private readonly fb = inject(FormBuilder).nonNullable;

  private readonly mesAtual = today().slice(0, 7);
  protected readonly form = this.fb.group({ de: [this.mesAtual], ate: [this.mesAtual] });
  protected readonly exportando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly exportado = signal<string | null>(null);

  protected exportar(): void {
    this.erro.set(null);
    this.exportado.set(null);
    if (this.exportando()) return;
    const { de, ate } = this.form.getRawValue();
    const meses = mesesNoIntervalo(de, ate);
    if (meses === null) {
      this.erro.set('Informe o mês inicial e o final.');
      return;
    }
    if (meses < 1) {
      this.erro.set('O mês final não pode ser anterior ao inicial.');
      return;
    }
    if (meses > EXPORTAR_MAX_MESES) {
      this.erro.set(`O período pode ter no máximo ${EXPORTAR_MAX_MESES} meses.`);
      return;
    }

    this.exportando.set(true);
    this.api.getFile(`/exports/entries.csv?from=${de}&to=${ate}`).subscribe({
      next: (resposta) => {
        this.exportando.set(false);
        const nome =
          nomeDoArquivo(resposta.headers.get('Content-Disposition')) ?? `fin-my-${de}_${ate}.csv`;
        baixar(resposta.body ?? new Blob(), nome);
        this.exportado.set(nome);
      },
      error: (error: unknown) => {
        this.exportando.set(false);
        void mensagemDoErro(error).then((m) => this.erro.set(m));
      },
    });
  }
}

function baixar(blob: Blob, nome: string): void {
  const endereco = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = endereco;
  a.download = nome;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(endereco));
}

/** Com `responseType: 'blob'` o problema+json chega como Blob: lê o `detail` dele. */
async function mensagemDoErro(error: unknown): Promise<string> {
  if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
    try {
      const problema = JSON.parse(await error.error.text()) as { detail?: string };
      if (problema.detail?.trim()) return problema.detail;
    } catch {
      return MENSAGEM_ERRO_GENERICO;
    }
    return MENSAGEM_ERRO_GENERICO;
  }
  return problemMessage(error);
}
