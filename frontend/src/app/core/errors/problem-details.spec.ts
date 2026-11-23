import { HttpErrorResponse } from '@angular/common/http';
import {
  MENSAGEM_ERRO_GENERICO,
  MENSAGEM_SEM_CONEXAO,
  asProblem,
  problemMessage,
} from './problem-details';

const erro = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

describe('problemMessage', () => {
  it('devolve o detail em pt-BR do ProblemDetails', () => {
    const e = erro(409, {
      code: 'email_already_registered',
      detail: 'Este e-mail já tem conta. Entrar ou recuperar senha?',
    });
    expect(problemMessage(e)).toBe('Este e-mail já tem conta. Entrar ou recuperar senha?');
  });

  it('usa mensagem genérica quando o corpo não é um ProblemDetails', () => {
    expect(problemMessage(erro(500, '<html>erro</html>'))).toBe(MENSAGEM_ERRO_GENERICO);
    expect(problemMessage(erro(500, null))).toBe(MENSAGEM_ERRO_GENERICO);
    expect(problemMessage(erro(502, { code: 'x', detail: '  ' }))).toBe(MENSAGEM_ERRO_GENERICO);
    expect(problemMessage(new Error('boom'))).toBe(MENSAGEM_ERRO_GENERICO);
  });

  it('avisa falta de conexão quando não há resposta (status 0)', () => {
    expect(problemMessage(erro(0, null))).toBe(MENSAGEM_SEM_CONEXAO);
  });
});

describe('asProblem', () => {
  it('expõe o code e campos extras', () => {
    const p = asProblem(erro(429, { code: 'login_locked', detail: 'x', retryAfterSeconds: 60 }));
    expect(p?.code).toBe('login_locked');
    expect(p?.retryAfterSeconds).toBe(60);
  });

  it('devolve null para o que não é erro HTTP com corpo de problema', () => {
    expect(asProblem(new Error('x'))).toBeNull();
    expect(asProblem(erro(500, 'texto'))).toBeNull();
    expect(asProblem(erro(500, { foo: 1 }))).toBeNull();
  });
});
