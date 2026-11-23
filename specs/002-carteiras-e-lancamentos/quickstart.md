# Quickstart — validar a 002

## 1. Subir

Como na 001 (`README.md`): `docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build`,
app em <http://localhost:8080>.

## 2. Suítes automáticas que precisam passar

| Suíte | Onde | Cobre |
|---|---|---|
| Vetores compartilhados | `FinMy.Domain.Tests/Money/*VectorTests`, `frontend/src/app/core/money/*.spec.ts` | RT-02, RT-05, normalização |
| `MonthSummaryCalculatorTests` | Domain.Tests | exemplo numérico da US3 (todos os números + abril), US3.2, filtro por carteira com transferência (US5.2) |
| `WalletConsistencyTests` | Api.Tests | SC-003 |
| `EntryRulesTests` | Domain.Tests | FR-012, RT-04 (US6.1, US6.2) |
| HTTP por recurso | Api.Tests `Wallets/`, `Entries/`, `Transfers/`, `Months/`, `Deletions/`, `Idempotency/` | cenários de aceite da spec |
| Isolamento | Api.Tests `Isolation/` | todo endpoint novo com id de outro Espaço → 404 |
| e2e | `frontend/e2e/` mobile e desktop | US1–US6 contra a stack real |

## 3. Roteiro manual (celular, 360 px)

1. Criar conta → app abre em `/mes/<mês atual>` vazio com convite para lançar.
2. Carteiras → "Azul", saldo inicial `500`, data 01/03/2026 → aparece como principal, saldo R$ 500,00.
   Tentar criar "azul" → "Já existe uma carteira com esse nome."
3. "+" → Saída `44,99` "Tim" → salva em ≤ 4 toques; aparece no mês como Paga.
4. "+" → Saída `29+73,2` → pré-visualiza "= R$ 102,20" e grava 102,20.
5. Reproduzir o exemplo da US3 em março/2026 e conferir o resumo; tocar a caixinha de "Duda" →
   Realizado vira 1.518,27 / 1.473,28 / 1.973,28. Ir para abril → saldo inicial 1.203,25 | 1.955,01.
6. Lançar Entrada Prevista "Carla atrasado" 300,00 em fevereiro; abrir o mês atual → aviso de
   pendência; "Trazer para este mês".
7. Transferir 20,00 de Azul para uma nova "Poupança" → totais do mês não mudam; filtro Azul mostra
   "Transferência enviada".
8. Excluir um lançamento e tocar "Desfazer" → volta idêntico. Duplicar "Tim" para o mês seguinte.
9. Busca "uber" (sem acento/maiúscula) → resultados de vários meses e total.
10. Desligar a rede e salvar → mensagem de sem conexão, formulário mantido.
11. Computador (1280 px): tabela do mês ordenável; clicar na situação alterna.
