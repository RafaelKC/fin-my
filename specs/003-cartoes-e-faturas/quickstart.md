# Quickstart — validar a 003

## Suítes automáticas

| Suíte | Cobre |
|---|---|
| Vetores `invoice-cycle`, `invoice-status`, `installments` (backend e front) | FR-003, FR-004, FR-006, FR-008, FR-012..FR-014; SC-002 (regra de fatura) |
| `InvoiceExampleTests` (Api.Tests) | exemplo numérico da US5 e US5.1–US5.7 |
| `CardScheduleChangeTests` | Edge Case de mudança de dias (research R3) |
| `InvoiceWalletSplitTests` | SC-004 |
| `WalletConsistencyTests` (atualizado) | SC-003 da 002 com pagamentos de fatura |
| Isolamento | rotas novas |
| e2e `cartoes`, `compra`, `parcelas`, `fatura`, `mes-faturas` | US1–US7 nos dois viewports |

## Roteiro manual (celular)

1. Cadastrar carteira "Azul" e cartão "Azul" (fecha 18, vence 25, paga pela Azul, limite 3.000).
2. "+" → aba Cartão (padrão) → `12,95` "Uber" → pré-visualização mostra a fatura e vencimento → salvar.
3. Compra "Casa China" `100` em 3x → pré-visualização "3x de R$ 33,34 (1ª) e R$ 33,33".
4. Parcelamento em andamento "Shoppe Casa" 5 de 8, `102,74` → 4 parcelas criadas.
5. Estorno `-99,90` → aviso de crédito.
6. Montar o exemplo da US5 (data fictícia via compras com datas de março/2026) e conferir Total 200,00;
   visão de março mostra "Fatura Azul" Previsto 200,00 / Realizado 0,00; tocar a caixinha → Paga.
7. Pagamento parcial, levar restante, conferir compras, mover compra para a próxima fatura.
8. Tela Cartões: limite disponível.
