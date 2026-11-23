# Vetores de teste compartilhados

Esta pasta guarda os **vetores de teste em JSON** que o backend (.NET) e o frontend (Angular) executam
com as mesmas entradas e as mesmas saídas esperadas. É o que garante que a pré-visualização da
interface (valor calculado, divisão de centavos, fatura de uma compra) bate com o servidor
(`docs/arquitetura.md` §5; constituição, princípio T2).

> Esta pasta nasce **só com este README** (feature 001). Os arquivos `*.json` entram a partir da
> feature 002, junto com a regra que cada um cobre. Já existem: `value-expression.json`,
> `money-split.json` e `text-normalize.json`.

## Regras gerais

- **Fonte da verdade é o vetor.** Se o backend e o frontend divergem, a regra está errada em um dos
  lados (ou o vetor está errado e é corrigido nos dois ao mesmo tempo, em um único PR).
- **Um arquivo por regra pura**, em `kebab-case`, com extensão `.json`, codificado em UTF-8.
- **Só dados, sem lógica.** Nada de expressões, referências a outros arquivos ou valores calculados em
  tempo de teste. Cada caso é autossuficiente.
- **Sem dados reais.** Descrições e valores são fictícios.

## Convenções de tipos

Seguem as convenções da API (`docs/arquitetura.md` §6) para que o mesmo valor possa ser usado nos
dois lados sem conversão.

| Conceito | Formato no JSON | Exemplo |
|---|---|---|
| Dinheiro | inteiro em centavos (`...Cents`), com sinal; **nunca** `number` fracionário | `"amountCents": 10220` |
| Texto de valor digitado | string, exatamente como a pessoa digitou | `"29+73,2"` |
| Data | string `"AAAA-MM-DD"` | `"2026-03-15"` |
| Mês | string `"AAAA-MM"` | `"2026-03"` |
| Hoje (relógio fixo) | `"today"`, data `"AAAA-MM-DD"` em America/Sao_Paulo | `"2026-03-15"` |
| Nomes de campo | `camelCase` | `closingDay` |

## Formato do arquivo

Cada arquivo é um objeto com um cabeçalho e uma lista de casos:

```json
{
  "rule": "MoneySplitter",
  "description": "Divisão de um valor em partes; a sobra de centavos vai para a 1ª parte (RT-02).",
  "cases": [
    {
      "id": "split-100-in-3",
      "description": "R$ 100,00 em 3 partes iguais",
      "input": { "totalCents": 10000, "parts": 3 },
      "expected": { "partsCents": [3334, 3333, 3333] }
    }
  ]
}
```

| Campo | Obrigatório | Significado |
|---|---|---|
| `rule` | sim | Nome da regra pura no domínio (ex.: `ValueExpressionParser`, `MoneySplitter`, `InvoiceCycle`, `InvoiceStatusRule`) |
| `description` | sim | Uma frase sobre o que o arquivo cobre e a regra/requisito de origem (RT-xx, FR-xxx) |
| `cases` | sim | Lista não vazia de casos |
| `cases[].id` | sim | Identificador único **dentro do arquivo**, estável, em `kebab-case`; aparece no nome do teste e nas mensagens de falha |
| `cases[].description` | sim | Cenário em linguagem natural, com a origem na spec quando houver |
| `cases[].input` | sim | Objeto com os argumentos da regra |
| `cases[].expected` | sim | Objeto com o resultado esperado **ou** um erro (ver abaixo) |

### Casos de erro

Quando a regra deve rejeitar a entrada, `expected` traz apenas `error`, com o **mesmo código estável**
usado pela API (`code` do `application/problem+json`, ex.: `value.invalid_expression`):

```json
{
  "id": "expression-division-by-zero",
  "description": "Divisão por zero é rejeitada",
  "input": { "text": "10/0" },
  "expected": { "error": "value.invalid_expression" }
}
```

`expected` nunca mistura resultado e `error`.

## Arquivos previstos

Os nomes abaixo são o ponto de partida (`docs/arquitetura.md` §5); o formato exato de `input` e
`expected` de cada um é definido na feature que o introduz e **documentado aqui no mesmo PR**.

| Arquivo | Regra | Origem | Feature |
|---|---|---|---|
| `value-expression.json` | `ValueExpressionParser`: texto digitado (`29+73,2`, `=1100-500`) → centavos, arredondamento meio-para-cima | RT-05, RT-02 | 002 |
| `money-split.json` | `MoneySplitter`: divisão em partes/parcelas, sobra para a 1ª | RT-02 | 002 |
| `text-normalize.json` | `TextNormalizer`: trim, minúsculas, sem acentos; espaços internos preservados | 002 FR-001, RT-05a | 002 |
| `invoice-cycle.json` | `InvoiceCycle`: datas de fechamento/vencimento e fatura de uma compra | 003 FR-003/004/008 | 003 |
| `invoice-status.json` | `InvoiceStatusRule`: situação da fatura (Total, Restante, hoje, fechamento, vencimento) | 003 FR-006 | 003 |

Formato de `input`/`expected` dos arquivos da feature 002:

| Arquivo | `input` | `expected` |
|---|---|---|
| `value-expression.json` | `{ "text": "29+73,2" }` | `{ "amountCents": 10220 }` ou `{ "error": "amount.invalid_expression" }` (também `amount.division_by_zero`, `amount.must_be_positive`, `amount.too_large`) |
| `money-split.json` | `{ "totalCents": 10000, "parts": 3 }` | `{ "partsCents": [3334, 3333, 3333] }` |
| `text-normalize.json` | `{ "text": "  Açaí  " }` | `{ "text": "acai" }` |

Formato dos arquivos da feature 003 (`today`, `date`, `closingDate`, `dueDate` em `AAAA-MM-DD`; meses em `AAAA-MM`):

| Arquivo | `input` | `expected` |
|---|---|---|
| `invoice-cycle.json` | `{ "date": "2026-03-17", "closingDay": 18, "dueDay": 25 }` | `{ "invoiceMonth": "2026-03", "closingDate": "2026-03-18", "dueDate": "2026-03-25" }` (fatura da compra e datas dessa fatura) |
| `invoice-status.json` | `{ "totalCents", "paidCents", "today", "closingDate", "dueDate" }` | `{ "status": "no_amount_due" \| "paid" \| "open" \| "closed" \| "overdue" }` |
| `installments.json` | `{ "mode": "total_amount" \| "installment_amount" \| "in_progress", "installmentCount", "firstNumber", "amountCents", "firstInvoiceMonth" }` (`firstNumber` = 1 fora do modo em andamento) | `{ "installments": [{ "number", "amountCents", "dueMonth" }] }` |

O vetor de valor usa o modo padrão do parser (resultado > 0). O modo que aceita zero e negativo (saldo
inicial de carteira) não está nos vetores.

Exemplos de valores esperados, tirados das specs (ilustram o formato; os arquivos reais são a
referência):

- `"29+73,2"` → `10220` centavos; `"=1100-500"` → `60000` (o `=` inicial é ignorado, 007).
- R$ 100,00 em 3 partes iguais → `[3334, 3333, 3333]` (004).
- R$ 110,94 em 4 parcelas → `[2775, 2773, 2773, 2773]` (004).

## Como os testes consomem os vetores

- **Backend:** os testes de `FinMy.Domain.Tests` leem os arquivos de `shared/test-vectors/` (caminho
  relativo à raiz do repositório) e geram um teste por caso, nomeado pelo `id`. Os arquivos não são
  copiados para dentro do projeto de testes manualmente.
- **Frontend:** os testes (Vitest/Jasmine, conforme o projeto) importam os mesmos arquivos e geram um
  teste por caso.
- Um caso sem teste correspondente em um dos lados, ou um `id` duplicado, deve **falhar** a suíte.

## Como adicionar ou alterar um vetor

1. Escreva o caso antes da regra (ou junto), a partir de um exemplo da spec ou de um caso de borda.
2. Use um `id` novo; nunca reaproveite o `id` de um caso removido para outro cenário.
3. Valide o JSON (`python -m json.tool arquivo.json` ou o formatador do editor).
4. Rode os testes do backend **e** do frontend; os dois lados precisam passar com o mesmo arquivo.
5. Se o formato de `input`/`expected` mudou, atualize a tabela de arquivos acima.
