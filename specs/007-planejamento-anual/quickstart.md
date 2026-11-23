# Quickstart — validar a 007

| Suíte | Cobre |
|---|---|
| Vetor `invoice-forecast` (back e front) | FR-010; SC-003 |
| `InvoiceEstimateTests` | US2.1–US2.6, FR-011, FR-012 (não afeta limite, acertos, fatura, saldo) |
| `PlanningGridTests` | US1.1, FR-002, SC-002 (grade == painel == mês) |
| `PlanningCellTests` | US1.2–US1.6, US1.8, US2.4, US2.5 |
| `PlanningCopyTests` | US3.1–US3.4 |
| e2e `planejamento.spec.ts` | US1 e US3 no computador; edição de célula no celular |

Manual: no computador, abrir `/planejamento/<ano seguinte>`, criar "Natal" 1.200 em dezembro e "13º
salário" 1.500 → Resultado de dezembro +300; estimativa de 800 no Azul de janeiro a dezembro; copiar do
ano anterior.
