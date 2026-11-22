# Feature Specification: Tags e acerto de contas

**Feature Branch**: `004-tags-e-acertos`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Uma coisa que quero diferente é que no lugar de pessoas seja tag."

> Glossário e regras transversais (RT-xx): ver `.specify/memory/constitution.md`.
> Depende de: 001, 002, 003.

## Como a planilha funciona hoje (referência)

Cada compra no cartão tem a coluna **Pessoa** (Lia, Duda, Carla, Bruna), escolhida de uma lista fixa.
O bloco **Pessoas** de cada aba soma o total de cartão por pessoa. No mês seguinte, cada pessoa
aparece como **Entrada** com o valor que ela gastou (`=INDIRECT(mês anterior!C40)`), às vezes com
ajustes (`+105,77`) — ou seja, as pessoas usam o cartão dela e devolvem o dinheiro. Compras
divididas entre pessoas viram várias linhas (`=239,8/2` para Lia e `=239,8/2` para Bruna).

No app, "Pessoa" vira **Tag**. Tags são livres (servem também como categoria: "Mercado", "Uber",
"Casa"). Tags que representam alguém que deve devolver dinheiro são marcadas como **Tag de acerto**,
e o app calcula quanto cobrar de cada uma e gera a Entrada de cobrança.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Marcar lançamentos com tags (Priority: P1)

A pessoa coloca uma ou mais tags num lançamento com um toque (tags mais usadas aparecem primeiro) ou
cria uma tag nova digitando o nome no próprio formulário.

**Why this priority**: substitui a coluna Pessoa e é base para todo o resto.

**Independent Test**: lançar uma compra com as tags "Bruna" e "Ifood" e filtrar o mês por "Bruna".

**Acceptance Scenarios**:

1. **Given** o formulário de lançamento aberto, **When** a pessoa abre o campo Tags, **Then** vê as tags ativas ordenadas por quantidade de usos nos últimos 90 dias (as 8 primeiras como botões de um toque) e um campo de busca.
2. **Given** não existe a tag "Farmácia", **When** a pessoa digita "Farmácia" no campo Tags e confirma, **Then** a tag é criada (não de acerto) e aplicada ao lançamento.
3. **Given** já existe a tag "Bruna", **When** a pessoa digita "bruna", **Then** o app usa a tag existente em vez de criar outra.
4. **Given** um lançamento com a Tag de acerto "Bruna", **When** a pessoa tenta adicionar a Tag de acerto "Carla" ao mesmo lançamento, **Then** o app impede e sugere "Dividir" (US4).
5. **Given** a visão do mês, **When** a pessoa filtra pela tag "Bruna", **Then** vê só os lançamentos (Entradas, Saídas e Compras das faturas do mês) com essa tag, e os totais do filtro.

---

### User Story 2 - Quanto cada um me deve (acerto por Tag de acerto) (Priority: P1)

Para cada Tag de acerto, a pessoa vê, por mês, quanto foi gasto em nome dela, quanto já foi cobrado
e quanto já foi recebido, e o saldo acumulado.

**Why this priority**: é o uso real do bloco "Pessoas" da planilha.

**Independent Test**: com o exemplo abaixo, conferir os números de "Bruna" em março.

**Exemplo numérico (vira teste automatizado)** — Tag de acerto "Bruna". Mês 2026-03:

| Lançamento | Onde conta | Valor |
|---|---|---|
| Compra "Ifood" Débito, fatura Verde 2026-03 | Gasto | 156,81 |
| Compra "Uber" Débito, fatura Verde 2026-03 | Gasto | 12,72 |
| Compra "Estorno" Crédito, fatura Verde 2026-03 | Gasto (negativo) | 9,53 |
| Saída "Presente Bruna" Paga, Mês de ref. 2026-03 | Gasto | 40,00 |
| Entrada "Bruna" Prevista, Mês de ref. 2026-03 | Cobrado | 150,00 |
| Entrada "Bruna Pix" Recebida, Mês de ref. 2026-03 | Cobrado e Recebido | 30,00 |

Bruna em março: Gasto = 156,81 + 12,72 − 9,53 + 40,00 = **200,00**; Cobrado = 150,00 + 30,00 =
**180,00**; Recebido = **30,00**; A cobrar = 200,00 − 180,00 = **20,00**; A receber = 200,00 − 30,00 =
**170,00**. Sem movimentos anteriores, o Saldo acumulado a receber até março = **170,00**.

**Acceptance Scenarios**:

1. **Given** o exemplo, **When** a pessoa abre "Acertos" em março/2026, **Then** vê para Bruna exatamente: Gasto 200,00; Cobrado 180,00; Recebido 30,00; A cobrar 20,00; A receber 170,00; Saldo acumulado 170,00.
2. **Given** o exemplo, **When** a Entrada "Bruna" (150,00) é marcada como Recebida, **Then** Recebido passa a 180,00, A receber a 20,00 e Saldo acumulado a 20,00.
3. **Given** o Saldo acumulado de Bruna é negativo (ela pagou a mais), **When** exibido, **Then** aparece como "Crédito da Bruna: R$ X".
4. **Given** uma fatura, **When** a pessoa a abre, **Then** vê subtotais por Tag de acerto e uma linha "Sem tag de acerto" (o que é da própria dona), somando o Total da fatura.

---

### User Story 3 - Gerar a cobrança com um toque (Priority: P1)

Na tela de acertos, a pessoa toca "Gerar cobrança" para uma Tag de acerto e o app cria a Entrada
Prevista com o valor "A cobrar".

**Why this priority**: substitui a fórmula `=INDIRECT(...C40)` copiada todo mês.

**Independent Test**: no exemplo da US2, gerar a cobrança de Bruna e conferir a Entrada criada.

**Acceptance Scenarios**:

1. **Given** o exemplo da US2 e hoje 20/03/2026, **When** a pessoa toca "Gerar cobrança" para Bruna em março, **Then** o app abre o formulário de Entrada já preenchido: descrição "Bruna — acerto 03/2026", valor 20,00, Mês de referência 2026-03, data 20/03/2026, Carteira principal, situação Prevista, tag Bruna; ao salvar, A cobrar passa a 0,00.
2. **Given** o mês da cobrança não é o mês de hoje, **When** a pessoa gera a cobrança, **Then** a data preenchida é o dia 1 daquele mês.
3. **Given** A cobrar ≤ 0, **When** exibido, **Then** o botão "Gerar cobrança" não aparece.
4. **Given** o formulário preenchido, **When** a pessoa altera valor ou data antes de salvar, **Then** prevalece o que ela digitou.

---

### User Story 4 - Dividir um lançamento entre pessoas (Priority: P2)

A pessoa lança uma compra que foi de várias pessoas (ex.: presente dividido entre ela e Bruna) e o app
cria um lançamento por parte, cada um com sua Tag de acerto, em partes iguais ou com valores
informados.

**Why this priority**: na planilha isso exigia várias linhas e contas manuais.

**Independent Test**: dividir R$ 239,80 entre "Sem tag de acerto" e "Bruna" em partes iguais.

**Acceptance Scenarios**:

1. **Given** o formulário com uma compra "Jacomar Niver Carla" de R$ 239,80, **When** a pessoa toca "Dividir", escolhe as partes "Eu (sem tag de acerto)" e "Bruna" em partes iguais e salva, **Then** são criadas duas compras de R$ 119,90, mesma descrição, data e cartão: uma sem Tag de acerto e outra com a tag Bruna; ambas mostram "Dividida em 2".
2. **Given** uma compra "MYNO" parcelada em 4x, **When** a pessoa divide com valores informados Eu 110,94, Carla 11,99 e Bruna 84,97 (total 207,90), **Then** são criados 3 parcelamentos de 4x independentes; o de "Eu" tem parcelas 27,75 + 27,73 + 27,73 + 27,73 (RT-02).
3. **Given** a divisão com valores informados, **When** a soma das partes difere do total, **Then** o app mostra a diferença e não salva.
4. **Given** partes iguais de R$ 100,00 em 3, **When** salva, **Then** as partes são 33,34, 33,33 e 33,33 na ordem em que as partes foram listadas (RT-02).
5. **Given** um lançamento dividido, **When** a pessoa edita ou exclui uma das partes, **Then** só aquela parte muda; as outras são lançamentos independentes.

---

### User Story 5 - Gerenciar tags (Priority: P3)

A pessoa renomeia, troca a cor, marca/desmarca como Tag de acerto, arquiva e junta tags.

**Why this priority**: manutenção.

**Independent Test**: juntar "Uber " e "Uber" e ver todos os lançamentos com uma única tag.

**Acceptance Scenarios**:

1. **Given** as tags "Mercado" e "Supermercado", **When** a pessoa junta "Supermercado" em "Mercado", **Then** todos os lançamentos de "Supermercado" passam a ter "Mercado" (sem duplicar se já tinham as duas) e "Supermercado" deixa de existir.
2. **Given** a tag "Bruna" (de acerto) e a tag "Carla" (não de acerto), **When** a pessoa marca "Carla" como de acerto e existem lançamentos com "Bruna" e "Carla" ao mesmo tempo, **Then** o app impede e lista esses lançamentos para correção.
3. **Given** uma tag usada em lançamentos, **When** a pessoa tenta excluí-la, **Then** só é possível arquivar (RT-11).
4. **Given** a junção de duas tags faria algum lançamento ficar com duas Tags de acerto, **When** a pessoa tenta juntar, **Then** o app impede e lista esses lançamentos.

---

### Edge Cases

- Compra parcelada com Tag de acerto: cada parcela conta como Gasto no mês da fatura em que cai (não o total no mês da compra), exatamente como a planilha fazia.
- Alterar a tag de uma parcela pergunta "só esta / todas" (003 FR-015).
- Lançamento sem tag nenhuma: válido; nos acertos conta como "Sem tag de acerto".
- Tag arquivada: continua nos lançamentos e nos relatórios, não aparece nas sugestões.
- Pagamentos de fatura e Transferências não recebem tags.

## Requirements *(mandatory)*

### Functional Requirements

**Tags**

- **FR-001**: Tag MUST ter: nome (1 a 30 caracteres, único no Espaço sem diferenciar maiúsculas/minúsculas e acentos), cor, "de acerto" (sim/não; padrão não), arquivada. Arquivamento/exclusão: RT-11.
- **FR-002**: Entradas, Saídas e Compras MUST aceitar zero ou mais tags, com no máximo uma Tag de acerto por lançamento.
- **FR-003**: O formulário de lançamento rápido MUST ter o campo Tags conforme os cenários 1.1 a 1.3. Ao escolher uma sugestão de descrição (002 FR-011), as tags do lançamento mais recente com essa descrição são aplicadas se o campo Tags não tiver sido alterado.
- **FR-004**: A visão do mês, a fatura e a busca MUST permitir filtrar por uma ou mais tags (lançamentos com qualquer uma delas) e mostrar os totais filtrados.
- **FR-005**: A pessoa MUST poder renomear, mudar cor, marcar/desmarcar "de acerto", arquivar/desarquivar e juntar tags, respeitando FR-002 (cenários 5.2 e 5.4).

**Acertos** (para cada Tag de acerto T e mês M)

- **FR-006**: **Gasto(T, M)** = soma das Saídas com T e Mês de referência M (qualquer situação) + soma das Compras de Débito com T em faturas com vencimento em M − soma das Compras de Crédito com T em faturas com vencimento em M.
- **FR-007**: **Cobrado(T, M)** = soma das Entradas com T e Mês de referência M (qualquer situação). **Recebido(T, M)** = soma das Entradas Recebidas com T e Mês de referência M.
- **FR-008**: **A cobrar(T, M)** = Gasto − Cobrado. **A receber(T, M)** = Gasto − Recebido. **Saldo acumulado(T, M)** = soma de A receber(T, m) para todos os meses m ≤ M.
- **FR-009**: A tela "Acertos" MUST mostrar, para o mês escolhido, uma linha por Tag de acerto ativa (e arquivadas com valores no mês) com os valores de FR-006 a FR-008, e permitir abrir a lista de lançamentos que compõem cada número.
- **FR-010**: "Gerar cobrança" (visível quando A cobrar > 0) MUST abrir o formulário de Entrada preenchido conforme os cenários 3.1 e 3.2; nada é salvo sem a confirmação da pessoa.
- **FR-011**: A fatura MUST mostrar subtotais por Tag de acerto e "Sem tag de acerto" (cada subtotal = Débitos − Créditos das compras daquela tag), cuja soma é o Total da fatura.

**Divisão**

- **FR-012**: O formulário de Saída e de Compra MUST oferecer "Dividir": a pessoa escolhe 2 a 10 partes, cada uma com uma Tag de acerto ou "Eu (sem tag de acerto)" (sem repetir), em modo "partes iguais" (RT-02, na ordem listada) ou "valores informados" (soma obrigatoriamente igual ao total).
- **FR-013**: Ao salvar, MUST ser criado um lançamento independente por parte, com os demais campos iguais (descrição, data, carteira/cartão, situação, parcelas, tags que não são de acerto). Em compra parcelada, cada parte vira um parcelamento próprio com o valor da parte como valor total (003 FR-013).
- **FR-014**: Partes de uma divisão MUST exibir "Dividida em N" e permitir ver as demais partes; fora isso, são independentes para edição e exclusão.

### Key Entities

- **Tag**: nome, cor, de acerto, arquivada.
- **Marcação**: ligação entre um lançamento (Entrada, Saída ou Compra) e uma Tag.
- **Grupo de divisão**: liga as partes criadas por uma divisão (apenas para exibição).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Colocar uma tag frequente num lançamento custa 1 toque.
- **SC-002**: O exemplo numérico da US2 passa como teste automatizado.
- **SC-003**: A pessoa gera as cobranças do mês de todas as pessoas em menos de 1 minuto (sem calculadora nem planilha).
- **SC-004**: Para toda fatura, a soma dos subtotais por Tag de acerto e "Sem tag de acerto" é igual ao Total da fatura.

## Assumptions

- A dona do Espaço é representada por "Sem tag de acerto"; não é preciso criar uma tag com o nome dela.
- Não existem categorias separadas; quem quiser categorizar usa tags comuns.
- Não há envio automático de cobrança (WhatsApp, e-mail) na v1.
