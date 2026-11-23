# Contrato das ferramentas MCP — 008

Endpoint: `POST/GET <APP_URL>/mcp` (Streamable HTTP, stateless), bearer OAuth (research R2).
Descrições das ferramentas e dos parâmetros em pt-BR. Datas `AAAA-MM-DD`, meses `AAAA-MM`, valores de
entrada como texto (RT-05: `"32,90"`, `"293,70"`); valores de saída em `*Cents` e `*Text`
(`"R$ 32,90"`). Nomes resolvidos por `CatalogResolver` (research R4).

Erro de ferramenta (`isError: true`): `{ "code": "...", "message": "<pt-BR>", "options": [...]? }`.
Códigos próprios: `ambiguous_or_not_found` (com `field` e `options`), `card_required`, `undo_not_allowed`;
demais = os `code` da API REST.

| Ferramenta | Parâmetros | Resposta | Handler / regra |
|---|---|---|---|
| `list_catalog` | — | `today`, `wallets[{name, isPrimary}]`, `creditCards[{name, closingDay, dueDay}]`, `tags[{name, isSettlement}]` (só ativos) | — |
| `get_wallet_balances` | — | `today`, `wallets[{name, currentBalanceCents, currentBalanceText}]`, total | 002 FR-003 |
| `get_month_summary` | `month?` (padrão: mês de hoje), `wallets?[]` | `today`, `planned{...}`, `actual{...}` (mesmos campos de `SummaryColumn`), `pending` | 002 FR-015 |
| `list_pending` | — | itens `{id, kind, description, date, referenceMonth, amount*}` | 002 FR-019 |
| `list_invoices` | `creditCard`, `from?`, `to?` (meses; padrão: 3 anteriores a 3 seguintes) | `[{dueMonth, closingDate, dueDate, status, total*, paid*, remaining*}]` | 003 |
| `get_invoice` | `creditCard`, `month?` (padrão: fatura atual), `includePurchases?` | fatura + compras opcionais + `settlementSubtotals` | 003, 004 FR-011 |
| `get_settlements` | `month?`, `tag?` | linhas de acerto | 004 FR-006..009 |
| `search_entries` | `text?`, `kinds?[]`, `wallets?[]`, `tags?[]`, `from?`, `to?`, `limit?` (≤ 50) | itens + `total*` + `totalCount` | 002 FR-025 (com compras) |
| `create_entry` | `kind` (`income|expense`), `description`, `amount`, `date?`, `wallet?`, `status?`, `referenceMonth?`, `tags?[]`, `split?` (só expense; `[{tag|null, amount?}]`, `mode`), `notes?`, `requestId?` | itens criados com todos os valores usados | 002, 004 |
| `create_card_purchase` | `description`, `amount` (negativo = estorno), `creditCard?`, `date?`, `installments?` (`{total, mode}`), `tags?[]`, `split?`, `notes?`, `confirmPaidInvoice?`, `requestId?` | compras criadas, cada uma com fatura (`dueMonth`, `dueDate`), `warnings` | 003, 004 |
| `create_transfer` | `fromWallet`, `toWallet`, `amount`, `date?`, `description?`, `requestId?` | transferência | 002 FR-021 |
| `undo_creation` | `id` (de um item criado) | `{ undone: n }` | research R4 |
| `set_entry_status` | `id`, `status` (`planned|settled`), `confirmed?` | `preview` ou entrada atualizada | 002 FR-006, FR-007 |
| `pay_invoice` | `creditCard`, `month?` (padrão: fatura vencida/fechada mais antiga em aberto, senão atual), `amount?`, `wallet?`, `date?`, `confirmed?` | `preview` ou fatura atualizada | 003 FR-017 |
| `generate_settlement_charge` | `tag`, `month?`, `amount?`, `date?`, `wallet?`, `confirmed?` | `preview` (os valores da 004 R4) ou entrada criada | 004 FR-010 |
| `delete_entry` | `id`, `type` (`entry|card_purchase|transfer`), `scope?` (parcelas), `confirmed?` | `preview` ou `{deleted: n}` | RT-09 |

Anotações: consultas `readOnlyHint: true`; `set_entry_status`, `pay_invoice`,
`generate_settlement_charge`, `delete_entry` `destructiveHint: true`.
