# Feature Specification: Lançamentos recorrentes (contas fixas, salário, assinaturas)

**Feature Branch**: `005-recorrencias`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Tão intuitivo e fácil de usar quanto a planilha" — na planilha, todo mês ela copia à mão as mesmas linhas: Salário, Adiantamento, Capital Social, Cesta de relacionamento, Tim, Seguro Vida, Prime Video, WellHub.

> Glossário e regras transversais (RT-xx): ver `.specify/memory/constitution.md`.
> Depende de: 001, 002, 003, 004.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Criar uma recorrência mensal (Priority: P1)

A pessoa cadastra uma conta fixa (ex.: "Tim", R$ 44,99, todo dia 10, Saída na carteira Azul) e o
app cria os lançamentos previstos nos próximos meses.

**Why this priority**: elimina o trabalho repetitivo de montar cada mês e dá a projeção do ano.

**Independent Test**: criar a recorrência e ver a Saída prevista em cada mês seguinte.

**Acceptance Scenarios**:

1. **Given** hoje é 05/03/2026, **When** a pessoa cria a recorrência Saída "Tim", R$ 44,99, dia 10, carteira Azul, início 2026-03, sem fim, **Then** existem Saídas Previstas "Tim" de R$ 44,99 nos dias 10 de cada mês de 2026-03 a 2027-12 (22 ocorrências: até dezembro do ano seguinte ao de hoje, para permitir planejar o ano seguinte inteiro — 007).
2. **Given** a recorrência acima, **When** hoje passa a ser em 2027, **Then** o app cria as ocorrências de 2028-01 a 2028-12 (mantém sempre até dezembro do ano seguinte ao de hoje).
3. **Given** a recorrência Compra "Prime Video" R$ 19,90, dia 2, cartão Azul (fecha 18, vence 25), **When** criada com início 2026-07, **Then** cada mês tem uma Compra "Prime Video" com data dia 2, na fatura determinada pela regra de fatura (003 FR-008) — ex.: compra 02/07/2026 → fatura 2026-07.
4. **Given** uma recorrência com dia 31, **When** gera a ocorrência de fevereiro, **Then** a data é o último dia de fevereiro (RT-03).
5. **Given** uma recorrência com término "6 ocorrências", **When** criada, **Then** existem exatamente 6 ocorrências e nenhuma depois.
6. **Given** a recorrência criada com dia 10 e início no mês de hoje, mas hoje já é dia 15, **When** criada, **Then** a ocorrência deste mês é criada com data dia 10 e situação Prevista (aparece como Atrasada, 002) — o app não marca nada como pago sozinho.

---

### User Story 2 - Salário que paga o mês seguinte (Priority: P1)

A pessoa cadastra o salário recebido no dia 25 para contar no mês seguinte, como faz hoje na planilha.

**Why this priority**: é o padrão real da planilha (Salário de 25/02 aparece na aba de março).

**Independent Test**: recorrência de Entrada com "Mês de referência: mês seguinte".

**Acceptance Scenarios**:

1. **Given** a recorrência Entrada "Salário" R$ 1.200,00, dia 25, carteira Azul, Mês de referência "mês seguinte à data", início 2026-09, **When** gerada, **Then** a ocorrência com data 25/09/2026 tem Mês de referência 2026-10, a de 25/10/2026 tem 2026-11, e assim por diante.
2. **Given** o valor do salário mudou só em outubro, **When** a pessoa edita a ocorrência de outubro para R$ 1.250,00, **Then** só ela muda e fica marcada como "alterada"; as demais continuam R$ 1.200,00.

---

### User Story 3 - Alterar ou encerrar uma recorrência (Priority: P2)

A pessoa muda o valor de uma conta fixa a partir de um mês (ex.: Tim passou para R$ 50,99 em
setembro) ou encerra uma assinatura.

**Why this priority**: contas fixas mudam algumas vezes por ano.

**Independent Test**: alterar valor a partir de 2026-09 e conferir ocorrências antes e depois.

**Acceptance Scenarios**:

1. **Given** a recorrência "Tim" R$ 44,99 com ocorrências de 2026-03 a 2027-12, a de 2026-08 Paga e a de 2026-11 alterada manualmente, **When** a pessoa altera o valor para R$ 50,99 "a partir de 2026-09", **Then** as ocorrências de 2026-09 a 2027-12 que estão Previstas e não foram alteradas manualmente passam a R$ 50,99; a de 2026-11 (alterada) e todas antes de 2026-09 não mudam.
2. **Given** a recorrência "WellHub", **When** a pessoa a encerra "a partir de 2026-10", **Then** as ocorrências de 2026-10 em diante que estão Previstas (ou, para Compras, em faturas cuja data de fechamento é posterior a hoje) e não foram alteradas manualmente são excluídas; as demais permanecem como lançamentos comuns; nenhuma nova é criada.
3. **Given** a pessoa exclui uma ocorrência isolada (ex.: dezembro), **When** o app estende o horizonte, **Then** a ocorrência excluída não é recriada.
4. **Given** uma ocorrência Paga, **When** a recorrência é alterada ou encerrada, **Then** a ocorrência Paga nunca é alterada nem excluída.

---

### User Story 4 - Transformar um lançamento em recorrência (Priority: P3)

A partir de um lançamento já existente, a pessoa escolhe "Repetir todo mês" e o app cria a
recorrência com os dados dele.

**Why this priority**: atalho de conveniência.

**Independent Test**: escolher "Repetir todo mês" numa Saída de 25/03/2026 e ver as ocorrências a partir de abril.

**Acceptance Scenarios**:

1. **Given** a Saída "Seguro Vida" R$ 25,99 de 25/03/2026, **When** a pessoa escolhe "Repetir todo mês", **Then** é criada a recorrência com dia 25, início 2026-03 e os demais dados do lançamento; o lançamento original passa a ser a ocorrência de 2026-03 (não é duplicado) e são criadas as ocorrências de 2026-04 em diante conforme FR-003.

---

### Edge Cases

- Carteira, cartão ou tag da recorrência arquivados: a recorrência para de gerar novas ocorrências e aparece com aviso "Revise: carteira/cartão arquivado"; as existentes permanecem.
- Recorrência de Compra parcelada: não suportada (recorrência gera sempre compras à vista).
- Alterar o dia da recorrência "a partir de M": muda a data das ocorrências afetadas (mesmas condições do cenário 3.1); para Compras, a fatura é recalculada (003 FR-008).
- Editar uma ocorrência só na situação (marcar Paga/Recebida) ou só conferida NÃO a marca como "alterada".
- Ocorrências são lançamentos comuns em todas as telas e cálculos (visão do mês, faturas, acertos); apenas exibem um ícone de recorrência.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Recorrência MUST ter: tipo (Entrada, Saída ou Compra), descrição, valor (RT-02, RT-05), dia do mês (1 a 31), Carteira (Entrada/Saída) ou Cartão (Compra), tags (004), mês inicial, término (sem fim; até um mês; ou após N ocorrências, 1 a 360) e, para Entrada/Saída, regra de Mês de referência ("mesmo mês da data" — padrão — ou "mês seguinte à data").
- **FR-002**: Frequência é sempre mensal na v1.
- **FR-003**: O sistema MUST manter ocorrências criadas como lançamentos reais do mês inicial até dezembro do ano seguinte ao de hoje (ou até o término, o que vier antes). A extensão do horizonte acontece automaticamente quando o ano de hoje muda, sem ação da pessoa.
- **FR-004**: Ocorrências de Entrada/Saída são sempre criadas como Previstas; ocorrências de Compra são compras à vista de Débito. A data é o dia da recorrência no mês da ocorrência (RT-03).
- **FR-005**: Editar uma ocorrência (exceto situação e conferida) MUST marcá-la como "alterada". Ocorrências alteradas não são afetadas por mudanças na recorrência.
- **FR-006**: Alterar uma recorrência pede "a partir de qual mês" (padrão: mês de hoje) e aplica a mudança às ocorrências daquele mês em diante que: não estão alteradas; e (Entrada/Saída) estão Previstas ou (Compra) estão em fatura cuja data de fechamento é posterior a hoje.
- **FR-007**: Encerrar uma recorrência "a partir de M" define o término em M − 1 e exclui as ocorrências de M em diante que atendem às condições da FR-006. Excluir a recorrência = encerrar a partir do mês de hoje; ocorrências restantes ficam como lançamentos comuns, sem vínculo.
- **FR-008**: Ocorrência excluída individualmente MUST NOT ser recriada.
- **FR-009**: A tela "Recorrências" MUST listar as recorrências ativas e encerradas com valor, dia, carteira/cartão, próxima ocorrência e total mensal previsto (Entradas e Saídas/Compras separados).
- **FR-010**: Lançamentos de Entrada, Saída e Compra à vista MUST oferecer "Repetir todo mês" (US4).

### Key Entities

- **Recorrência**: tipo, descrição, valor, dia, carteira ou cartão, tags, mês inicial, término, regra de Mês de referência, situação (ativa/encerrada).
- **Ocorrência**: um lançamento (Entrada, Saída ou Compra) ligado à Recorrência, com o mês da ocorrência e a marcação "alterada".

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Depois de cadastradas as recorrências, um mês novo já começa com todas as contas fixas previstas, sem nenhuma digitação.
- **SC-002**: Os cenários das US1 a US3 passam como testes automatizados.
- **SC-003**: Cadastrar uma recorrência leva menos de 30 segundos.

## Assumptions

- Recorrências semanais, anuais ou a cada N meses ficam fora da v1.
- Valores variáveis (ex.: salário que muda todo mês) são tratados editando a ocorrência do mês.
