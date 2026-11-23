# E2E (Playwright)

Rodam em dois viewports: `mobile` (360×800) e `desktop` (1280×800). Como rodar, em
[`../../README.md`](../../README.md#e2e-playwright-360-px-e-1280-px).

- Arquivos que dizem **"API simulada"** usam `page.route`; rodam também com o `ng serve`.
- Testes **"stack real"** e `pwa.spec.ts` precisam do compose no ar e de `E2E_BASE_URL` (pulados com `test.skip` quando ele não está definido).
- `playwright.config.ts` usa `serviceWorkers: 'block'`: o service worker do build de produção
  intercepta as requisições antes do `page.route`. Só `pwa.spec.ts` liga o worker (`test.use`).

## Roteiro manual do quickstart §3 → e2e

| Passo                                                        | Cobertura                                                                                           |
| ------------------------------------------------------------ | --------------------------------------------------------------------------------------------------- |
| 1. Criar conta, ver "Finanças de Ana" (US1.1)                | `roteiro-quickstart.spec.ts` (real) · `criar-conta.spec.ts`                                         |
| 2. Mesmo e-mail em maiúsculas (US1.2)                        | `roteiro-quickstart.spec.ts` (real) · `criar-conta.spec.ts`                                         |
| 3. 5 senhas erradas, 6ª bloqueia 15 min (US2.5)              | `roteiro-quickstart.spec.ts` (real) · `entrar.spec.ts`                                              |
| 4. Trocar a senha num aparelho derruba o outro               | `perfil.spec.ts` (real, dois contextos)                                                             |
| 5. Recuperar senha pelo Mailpit, link de uso único (US4)     | `recuperar-senha.spec.ts` (real)                                                                    |
| 6. E-mail inexistente: mesma mensagem, nenhum e-mail (US4.2) | `roteiro-quickstart.spec.ts` (real, com controle positivo)                                          |
| 7. Fechar e reabrir o navegador continua logado (US2.3)      | `entrar.spec.ts` (real)                                                                             |
| 8. Instalar como PWA                                         | `pwa.spec.ts` (manifest instalável + service worker registrado, sem cache de API) · `casca.spec.ts` |

O passo 8 **no aparelho** ("Adicionar à tela inicial" e abrir em tela cheia) continua manual: o
Playwright não aciona o prompt de instalação. O e2e garante o que o navegador exige para oferecê-lo.
