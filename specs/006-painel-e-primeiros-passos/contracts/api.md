# Contrato — 006 (REST, convenções da 002–005)

| Método e rota | Resposta | Notas |
|---|---|---|
| `GET /years/{yyyy}/panel` | `{ year, today, months: [{ month, planned: SummaryColumn, actual: SummaryColumn }] (12), totals: { planned: {incomeCents, expenseCents, resultCents, finalBalanceCents}, actual: {…} } }` | research R1; ano 2000–2100 senão 422 |
| `GET /now` | `{ today, wallets: [{ id, name, color, currentBalanceCents }], archivedWalletsBalanceCents, totalBalanceCents, invoices: [{ creditCardId, creditCardName, dueMonth, dueDate, status, remainingCents }], pending: { count, toReceiveCents, toPayCents }, settlements: [{ tagId, tagName, accumulatedCents }] }` | research R2 |
| `GET /onboarding` | `{ steps: { wallets?, cards?, settlement_tags?, installments?, recurrences? }, dismissed, autoStart }` | research R3 |
| `PUT /onboarding/steps/{step}` | corpo `{ state: done\|skipped }` → o mesmo objeto | step inválido → 422 |
| `POST /onboarding/dismiss` | o mesmo objeto | |
| `GET /exports/entries.csv?from=&to=` | CSV (research R4) | intervalo inválido ou > 120 meses → 422 |

## Telas

| Rota | Tela |
|---|---|
| `/painel/:ano` (`/painel` → ano de hoje) | Topo "Agora": carteiras e total (linha "Arquivadas"), faturas próximas, pendências, acertos ("Bruna te deve R$ X" / "Crédito da Bruna: R$ X"); cartão "Continuar configuração" quando houver guia pendente. Tabela `‹ 2026 ›` com 12 meses + Total, alternância Previsto/Realizado, negativos em vermelho, mês de hoje destacado, toque abre `/mes/AAAA-MM`; gráfico (≥ 1024 px ao lado/acima; celular abaixo da tabela) |
| `/primeiros-passos/:etapa` | Guia de 5 etapas com progresso, "Continuar", "Pular", "Dispensar guia"; ao final → `/mes/<hoje>`; textos da US3 (etapa Carteiras: "informe o saldo que está na conta hoje") |
| `/perfil` (alteração) | "Tela inicial: Mês / Painel" (preferência do aparelho) e "Exportar lançamentos (CSV)" com mês inicial e final |

Casca: "Painel" entra no menu lateral e em "Mais".
