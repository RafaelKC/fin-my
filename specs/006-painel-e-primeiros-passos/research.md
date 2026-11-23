# Research — 006 Painel, resumo de agora e primeiros passos

Sem regra de cálculo nova: tudo reaproveita `MonthSummaryCalculator` (002/003), saldo de carteira
(002 FR-003), faturas (003), pendências (002) e acertos (004).

## R1. Painel do ano

- `GET /years/{yyyy}/panel` → 12 meses com `planned`/`actual` (`SummaryColumn` da 002) e `totals`
  (`incomeCents`, `expenseCents`, `resultCents` somados; `finalBalanceCents` = o de dezembro), sem filtro
  de carteira. Implementação: as mesmas consultas agregadas do `GET /months/{m}`, uma vez para o ano
  (meses ≤ dez/yyyy), e o calculador compõe os 12 meses; nunca 12 chamadas do endpoint do mês.
- Teste de paridade SC-001: para dados de ≥ 3 meses com faturas, pagamentos, transferências e
  pendências, cada mês do painel == `GET /months/{m}` sem filtro. Teste de desempenho SC-003 com 3.000
  lançamentos (p95 local < 2 s, registrado no PR).

## R2. Resumo de agora (FR-005)

- `GET /now` → `today`; `wallets` (ativas com saldo), `archivedWalletsBalanceCents` (soma das arquivadas
  com saldo ≠ 0, linha "Arquivadas"), `totalBalanceCents` (todas); `invoices` com Restante > 0 e
  `dueDate` entre hoje − 30 e hoje + 30 dias, ordem de vencimento (cartão, mês, vencimento, situação,
  restante); `pending` (como na 002); `settlements` = Saldo acumulado até o mês de hoje por tag de acerto,
  só ≠ 0.

## R3. Guia de primeiros passos (FR-007)

- Estado por Espaço em colunas novas de `tenants` (tabela global): `onboarding_steps jsonb not null
  default '{}'` (chaves `wallets`, `cards`, `settlement_tags`, `installments`, `recurrences` → `"done"`
  ou `"skipped"`) e `onboarding_dismissed_at timestamptz null`. Migração `Onboarding`.
- `GET /onboarding` → `{ steps, dismissed, autoStart }`; `autoStart = !dismissed && Espaço sem
  carteiras && alguma etapa pendente`. `PUT /onboarding/steps/{step} { state: done|skipped }`;
  `POST /onboarding/dismiss`.
- Etapa "concluída" = a pessoa tocou "Continuar" na etapa (não é inferido dos dados). O guia abre
  sozinho só com `autoStart` (logo após criar a conta); depois disso só pelo cartão "Continuar
  configuração" no Painel, visível enquanto houver etapa pendente e não dispensado.
- As etapas reaproveitam os formulários existentes (carteira, cartão, tag de acerto, compra em
  andamento, recorrência) embutidos na rota `/primeiros-passos/:etapa`.

## R4. Exportação CSV (FR-008)

- `GET /exports/entries.csv?from=AAAA-MM&to=AAAA-MM` → `text/csv; charset=utf-8` com BOM (o Excel
  reconhece UTF-8), `Content-Disposition: attachment; filename="fin-my-AAAA-MM_AAAA-MM.csv"`.
  Intervalo máximo 120 meses.
- Colunas (cabeçalho exatamente): `Tipo;Descrição;Data;Mês de referência;Valor;Situação;Carteira;Cartão;Fatura;Parcela;Tags`.
  - `Tipo`: Entrada, Saída, Compra, Pagamento de fatura, Transferência.
  - Período: Entrada/Saída pelo Mês de referência; Compra e Pagamento de fatura pelo mês de vencimento
    da fatura; Transferência pelo mês da data.
  - `Mês de referência`: `MM/AAAA` para Entrada/Saída e Pagamento de fatura (mês de vencimento); vazio
    para Compra e Transferência.
  - `Valor`: `1234,56` sem milhar; Entrada +, Saída −, Compra débito −, Compra crédito +, Pagamento −,
    Transferência + (neutra).
  - `Situação`: Prevista/Recebida/Paga; Compra vazio; Pagamento "Paga"; Transferência "Efetivada".
  - `Carteira`: nome; Transferência `Origem → Destino`. `Cartão`/`Fatura` (`MM/AAAA`) só em Compra e
    Pagamento. `Parcela` `k/N`. `Tags`: nomes separados por vírgula.
  - Campos com `;`, `"` ou quebra de linha entre aspas (RFC 4180). Ordem: mês, data, criação.
- Front baixa com `HttpClient` (`responseType: 'blob'`, bearer) e salva com nome do cabeçalho.

## R5. Gráfico

- Componente SVG próprio (`shared/chart-anual/`): barras Entradas x Saídas por mês e linha do Saldo
  final, cores do tema, rótulos acessíveis (`<title>`), sem biblioteca externa (Princípio V).

## R6. Tela inicial (FR-006)

- Preferência do aparelho `homeScreen: 'mes' | 'painel'` em `finmy.prefs.v1` (002 R12), escolhida no
  Perfil; `/` redireciona conforme ela.
