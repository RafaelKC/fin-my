# Research — 003 Cartões, Compras parceladas e Faturas

Normas gerais em `docs/arquitetura.md`; decisões da 002 em `specs/002-carteiras-e-lancamentos/research.md`
(parser, idempotência, desfazer, `version`, sugestões) continuam valendo.

## R1. Regras puras e vetores

- `FinMy.Domain/Cards/InvoiceCycle` (puro):
  - `ClosingDate(dueMonth, closingDay, dueDay)`: se `closingDay < dueDay` → dia `closingDay` em `dueMonth`;
    senão (inclusive `closingDay == dueDay`) → dia `closingDay` no mês anterior; dia inexistente →
    último dia do mês (RT-03).
  - `DueDate(dueMonth, dueDay)`.
  - `InvoiceFor(date, closingDay, dueDay) -> YearMonth`: menor mês V cujo `ClosingDate(V) > date`
    (testar V = mês de `date`, +1, +2; nunca passa de +2).
  - `CurrentInvoice(today, …) = InvoiceFor(today, …)` (FR-007).
- `InvoiceStatusRule.Evaluate(totalCents, paidCents, today, closingDate, dueDate)` → `no_amount_due |
  paid | open | closed | overdue` (FR-006, nessa ordem).
- `InstallmentGenerator.Generate(mode, n, firstNumber, amountCents, firstInvoiceMonth)` → lista
  `(k, amountCents, dueMonth)`; `total_amount` usa `MoneySplitter` (sobra na 1ª parcela gerada);
  `in_progress` gera `p..N` a partir de `firstInvoiceMonth`.
- Vetores: `shared/test-vectors/invoice-cycle.json` (todos os exemplos de "Regra de fatura" da spec,
  cartões Azul, Verde, X e Y, mais 31/01 com fechamento 30, fevereiro bissexto 2028),
  `invoice-status.json` (as 5 situações e as fronteiras: hoje = fechamento → closed; hoje = vencimento
  → closed; total 0 → no_amount_due; pago antes do fechamento → paid), `installments.json`
  (US3.1, US3.2, US4.1). O front porta `InvoiceCycle` e `InstallmentGenerator` (pré-visualização do
  formulário, FR-010, US3.3) e roda os mesmos vetores.

## R2. Linha de fatura (`invoices`)

- Criada sob demanda por `InvoiceRepository.GetOrCreate(cardId, dueMonth)` com
  `INSERT … ON CONFLICT (tenant_id, credit_card_id, due_month) DO NOTHING` + `SELECT` (o índice único
  inclui `tenant_id`). Datas gravadas com `InvoiceCycle` no momento da criação.
- A API identifica fatura por **cartão + mês** (`/credit-cards/{id}/invoices/{AAAA-MM}`), exista a
  linha ou não; uma fatura sem linha é devolvida com total 0, datas calculadas e `id: null`.
- Fatura não é excluída nem tem soft delete na prática (as colunas padrão existem, nunca preenchidas).

## R3. Mudança de dias do cartão (Edge Case)

- `PUT /credit-cards/{id}` com novo `closingDay`/`dueDay`: numa transação, para cada fatura do cartão
  com `closing_date > hoje`, recalcula `closing_date`/`due_date`; depois, para cada compra dessas
  faturas com `manually_moved = false`: à vista → `InvoiceFor(purchase_date)`; parcela k de plano
  `total_amount`/`installment_amount` → `InvoiceFor(purchase_date) + (k − 1)`; parcela de plano
  `in_progress` → `first_invoice_due_month + (k − first_number)` (não muda). Nenhuma compra vai para
  fatura com `closing_date ≤ hoje`; se a regra mandar para uma, fica na fatura atual.
  Teste dedicado.

## R4. Pagamentos e concorrência

- Criar/editar pagamento: `SELECT … FOR UPDATE` na linha da fatura, recalcula Restante e valida
  `valor ≤ Restante (+ valor atual na edição)` → 422 `invoice.payment_exceeds_remaining`
  ("O valor em aberto é R$ X"). Valor padrão = Restante; se Restante ≤ 0 → 422
  `invoice.nothing_to_pay`.
- Marcar caixinha da fatura = `POST …/payments` com corpo vazio (padrões FR-017). Desmarcar =
  `DELETE …/payments` (todos, um lote, devolve `deletionBatchId`).
- Saldo atual da carteira (002 FR-003) e o teste SC-003 da 002 passam a descontar pagamentos.

## R5. Compra em fatura já paga (US5.7)

- Se a fatura de destino (de qualquer parcela) está `paid` e `hoje ≥ closing_date`, o `POST`/`PUT`
  responde 409 `card_purchase.invoice_paid_confirmation_required` com `detail` "Esta fatura já foi paga
  e voltará a ter valor em aberto" — o cliente repete com `"confirmPaidInvoice": true`.

## R6. Natureza e estorno

- Valor digitado negativo na compra (o parser aceita sinal neste campo) → `nature = credit`, valor
  absoluto, resposta com `warnings: ["recorded_as_credit"]` (front mostra "Lançado como
  estorno/crédito"). Crédito com parcelas > 1 → 422 `card_purchase.credit_must_be_single`.

## R7. Escopo de edição/exclusão/movimento em parcelas

- Parâmetro `scope`: `this | all` (descrição, observação), `this | this_and_following | all` (excluir,
  mover). Cartão e data da compra sempre afetam todas (FR-015) e redistribuem pela regra de R3 (sem a
  restrição de fatura fechada: a pessoa está corrigindo). Alterar `installmentTotal` ou o valor total
  → `PUT /installment-plans/{id}`, que recria as parcelas (soft delete das antigas no mesmo lote,
  novas criadas) e é recusado com 409 `installment_plan.has_paid_invoice` se alguma parcela está em
  fatura `paid`.
- Exclusão de compra do par "saldo levado" exclui as duas no mesmo lote.
- Mover: `POST /card-purchases/{id}/move { direction: previous|next, scope }` → `manually_moved = true`.

## R8. Visão do mês (002 FR-015 + FR-021..FR-023)

- `MonthSummaryCalculator` recebe, por mês ≤ M e por fatura: Total, Pago por carteira e carteira de
  pagamento do cartão. Previsto da fatura (todas as carteiras) = `max(Total, Pago, 0)`; Realizado =
  Pago. Com filtro de carteiras S: Previsto = Σ_{X∈S} pagos com X + (pagamento ∈ S ? max(Restante, 0) : 0);
  Realizado = Σ_{X∈S} pagos com X. Teste SC-004: soma por carteira = valor sem filtro.
- `GET /months/{m}` ganha `invoices[]` (faturas com vencimento em M com total ≠ 0 ou com pagamentos),
  cada uma com `plannedCents`/`actualCents` já no recorte do filtro. Faturas zeradas e sem pagamento não
  aparecem.
- Ponto de extensão da 007: `estimate_cents` entra aqui depois.

## R9. Sugestões e formulário

- `GET /entries/suggestions` aceita `kind=card_purchase`; o universo passa a incluir `card_purchases`
  (descrição base). Resposta ganha `creditCardId` (compra mais recente com essa descrição; `null` se
  arquivado). Para `income`/`expense`, compras contam nos `uses` mas não fornecem carteira.
- Tipo padrão no primeiro uso do aparelho passa a ser "Cartão" (FR-010); cartão padrão = último usado
  no aparelho (`lastCardId` nas preferências) ou o primeiro ativo em ordem alfabética.

## R10. Limite disponível e tela de cartões

- `GET /credit-cards` devolve por cartão: `currentInvoice` (mês, total, closingDate), `nextInvoice`
  (mês, total), `openPastInvoices` (faturas com `closing_date ≤ hoje`, situação closed/overdue, com
  restante), `availableLimitCents` (FR-026; `null` sem limite). Uma consulta agregada por cartão.
