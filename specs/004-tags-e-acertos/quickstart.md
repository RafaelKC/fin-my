# Quickstart — validar a 004

## Suítes automáticas

| Suíte | Cobre |
|---|---|
| `SettlementCalculatorTests` (Domain) | exemplo numérico da US2 (SC-002), US2.2, US2.3, acumulado de vários meses, parcela contando no mês da fatura |
| `SplitPlannerTests` (Domain) | US4.1–US4.4 |
| HTTP `Tags/`, `Settlements/`, `Splits/` | US1.2–US1.4, US2.1, US4, US5.1–US5.4 |
| `InvoiceSettlementSubtotalsTests` | FR-011, SC-004 |
| Isolamento | rotas novas e `tagIds` de outro Espaço → 404 |
| e2e `tags`, `acertos`, `dividir`, `filtro-tags` | US1–US5 nos dois viewports |

## Roteiro manual (celular)

1. Lançar compra com as tags "Bruna" (criar como de acerto em `/tags`) e "Ifood" — 1 toque nas chips.
2. Digitar "bruna" no campo → usa a existente. Tentar adicionar "Carla" (de acerto) → aviso com Dividir.
3. Montar o exemplo da US2 em março/2026 e conferir `/acertos/2026-03`; marcar a Entrada "Bruna" como
   recebida → números da US2.2.
4. "Gerar cobrança" → formulário pré-preenchido; salvar → A cobrar 0,00.
5. Dividir R$ 239,80 entre Eu e Bruna → duas compras de 119,90 com "Dividida em 2".
6. Juntar "Supermercado" em "Mercado".
7. Fatura mostra "Por pessoa" somando o Total.
