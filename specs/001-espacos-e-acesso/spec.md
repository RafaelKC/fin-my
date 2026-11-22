# Feature Specification: Contas de usuário e Espaços isolados

**Feature Branch**: `001-espacos-e-acesso`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "App financeiro para minha esposa usar no celular e na web. Quero ter uma conta para eu usar também, mas não no perfil dela: separação de tenant."

> Glossário e regras transversais (RT-xx): ver `.specify/memory/constitution.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Criar conta e começar a usar (Priority: P1)

Uma pessoa acessa o app pela primeira vez, cria a conta com nome, e-mail e senha e já entra no seu
próprio Espaço, vazio, pronto para cadastrar carteiras, cartões e lançamentos.

**Why this priority**: sem conta e sem Espaço nada mais funciona.

**Independent Test**: criar uma conta nova e verificar que a pessoa entra logada num Espaço vazio.

**Acceptance Scenarios**:

1. **Given** um e-mail ainda não cadastrado, **When** a pessoa informa nome, e-mail e uma senha válida e confirma, **Then** a conta é criada, um Espaço chamado "Finanças de <primeiro nome>" é criado com ela como dona, e ela entra no app já logada.
2. **Given** um e-mail já cadastrado, **When** alguém tenta criar conta com ele, **Then** o app mostra "Este e-mail já tem conta. Entrar ou recuperar senha?" e não cria nada.
3. **Given** uma senha com menos de 8 caracteres, **When** a pessoa tenta criar a conta, **Then** o app explica o requisito e não cria a conta.

---

### User Story 2 - Entrar e continuar logado no celular (Priority: P1)

A pessoa entra com e-mail e senha e continua logada no celular por longos períodos, para que lançar
um gasto não exija digitar senha toda vez.

**Why this priority**: a rapidez de lançamento (Princípio I) depende de não precisar logar a cada uso.

**Independent Test**: entrar, fechar o app, reabrir no dia seguinte e verificar que continua logado.

**Acceptance Scenarios**:

1. **Given** uma conta existente, **When** a pessoa informa e-mail e senha corretos, **Then** entra no seu Espaço.
2. **Given** e-mail ou senha incorretos, **When** a pessoa tenta entrar, **Then** vê "E-mail ou senha incorretos" (sem dizer qual dos dois está errado).
3. **Given** a pessoa entrou num aparelho, **When** ela reabre o app nesse aparelho em até 90 dias desde o último uso, **Then** continua logada sem digitar a senha.
4. **Given** a pessoa ficou mais de 90 dias sem usar o app naquele aparelho, **When** reabre, **Then** precisa entrar novamente.
5. **Given** 5 tentativas de senha erradas seguidas para o mesmo e-mail em 15 minutos, **When** ocorre a 6ª tentativa, **Then** o app bloqueia novas tentativas para esse e-mail por 15 minutos e informa isso.

---

### User Story 3 - Isolamento entre Espaços (Priority: P1)

Léo e Lia têm, cada um, sua própria conta e seu próprio Espaço. Nada que um cadastra aparece
para o outro.

**Why this priority**: requisito explícito do pedido e Princípio II da constituição.

**Independent Test**: com duas contas, cadastrar dados em uma e verificar que a outra não vê nada,
nem por busca, autocompletar, relatório ou acesso direto a um endereço/identificador.

**Acceptance Scenarios**:

1. **Given** duas contas A e B, **When** A cadastra uma carteira, um cartão, uma tag e um lançamento, **Then** B não vê nenhum deles em nenhuma tela, lista, busca ou sugestão.
2. **Given** a pessoa B obtém o endereço (link) de um lançamento de A, **When** B tenta abri-lo, **Then** vê "Não encontrado", igual a um lançamento que não existe.
3. **Given** A tem a descrição "Uber" usada 50 vezes, **When** B digita "Ub" no campo descrição, **Then** B não recebe "Uber" como sugestão vinda dos dados de A.

---

### User Story 4 - Recuperar senha (Priority: P2)

A pessoa esqueceu a senha e recebe por e-mail um link para cadastrar uma nova.

**Why this priority**: necessário para uso real, mas não bloqueia o desenvolvimento das outras features.

**Independent Test**: pedir recuperação, usar o link recebido, definir nova senha e entrar com ela.

**Acceptance Scenarios**:

1. **Given** um e-mail cadastrado, **When** a pessoa pede recuperação, **Then** recebe um e-mail com link válido por 1 hora e uso único.
2. **Given** um e-mail NÃO cadastrado, **When** alguém pede recuperação, **Then** o app mostra a mesma mensagem do caso anterior ("Se houver conta com este e-mail, enviaremos um link") e nenhum e-mail é enviado.
3. **Given** a pessoa definiu nova senha pelo link, **When** a troca é concluída, **Then** todas as sessões abertas em outros aparelhos são encerradas.
4. **Given** um link já usado ou com mais de 1 hora, **When** a pessoa o abre, **Then** vê que o link expirou e pode pedir outro.

---

### User Story 5 - Perfil e sair (Priority: P3)

A pessoa altera nome, senha e o nome do Espaço, e pode sair do app em um aparelho.

**Why this priority**: conveniência.

**Independent Test**: alterar nome e senha, sair, entrar com a nova senha.

**Acceptance Scenarios**:

1. **Given** a pessoa logada, **When** altera a senha informando a senha atual corretamente, **Then** a nova senha passa a valer e as sessões em outros aparelhos são encerradas.
2. **Given** a pessoa logada, **When** toca em "Sair", **Then** a sessão daquele aparelho é encerrada e os dados deixam de ser exibidos nele.
3. **Given** a pessoa logada, **When** renomeia o Espaço, **Then** o novo nome aparece no topo do app.

---

### Edge Cases

- E-mail é comparado sem diferenciar maiúsculas/minúsculas e sem espaços nas pontas (`Lia@X.com ` = `lia@x.com`).
- Duas criações de conta simultâneas com o mesmo e-mail: apenas uma é criada; a outra recebe a mensagem do cenário 1.2.
- A pessoa está com o app aberto em dois aparelhos e troca a senha em um: o outro é deslogado na próxima ação e o que estava sendo digitado não é salvo, mas permanece no formulário após novo login (Restrições de tecnologia da constituição).
- Exclusão de conta: fora de escopo na v1 (ver Assumptions).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: O sistema MUST permitir criar conta com nome (1 a 60 caracteres), e-mail válido e senha.
- **FR-002**: A senha MUST ter no mínimo 8 caracteres e no máximo 128. Nenhuma outra regra de composição é exigida.
- **FR-003**: O e-mail MUST ser único no sistema, comparado conforme o primeiro item de Edge Cases.
- **FR-004**: Ao criar a conta, o sistema MUST criar automaticamente um Espaço com a pessoa como dona e nome "Finanças de <primeiro nome>".
- **FR-005**: O acesso a um Espaço MUST ser concedido por **participação**: um Usuário participa de um Espaço com um papel. Na v1 existe apenas o papel **Dono**, cada Espaço tem exatamente um participante (o Dono que o criou) e cada Usuário participa de exatamente um Espaço. Não existe convite, compartilhamento nem troca de Espaço na interface da v1.
- **FR-005a** (preparação para compartilhamento): toda verificação de acesso MUST ser feita pela pergunta "este Usuário participa deste Espaço?" — nunca por "este Usuário é o dono deste dado". Todo lançamento e cadastro MUST registrar o Usuário que o criou e o último que o alterou, e quando. Preferências pessoais (tela inicial, últimos tipo/carteira/cartão usados) pertencem ao Usuário/aparelho, não ao Espaço. Isso permite, numa versão futura, convidar outra pessoa para o mesmo Espaço e um Usuário participar de vários Espaços sem reestruturar os dados.
- **FR-006**: Todo dado financeiro MUST pertencer a um Espaço e só ser acessível por quem participa desse Espaço (RT-01). Na v1, isso significa: só pelo Dono.
- **FR-007**: Tentativas de acessar dados de outro Espaço MUST ter o mesmo resultado de dados inexistentes ("Não encontrado").
- **FR-008**: Sugestões, buscas, totais, relatórios e exportações MUST considerar apenas dados do Espaço da pessoa logada.
- **FR-009**: A sessão MUST permanecer ativa num aparelho enquanto houver uso nos últimos 90 dias; depois disso, exige novo login.
- **FR-010**: O sistema MUST bloquear tentativas de login por 15 minutos para um e-mail após 5 falhas consecutivas em 15 minutos.
- **FR-011**: A recuperação de senha MUST usar link por e-mail com validade de 1 hora e uso único, sem revelar se o e-mail existe.
- **FR-012**: Trocar ou redefinir senha MUST encerrar todas as outras sessões da pessoa.
- **FR-013**: O sistema MUST permitir alterar nome do Usuário e nome do Espaço (1 a 60 caracteres).
- **FR-014**: Mensagens de erro de login MUST NOT indicar se foi o e-mail ou a senha que estava errado.

### Key Entities

- **Usuário**: nome, e-mail (único), senha (guardada de forma irrecuperável), data de criação.
- **Espaço**: nome, data de criação. É a fronteira de isolamento de todos os dados.
- **Participação**: Usuário + Espaço + papel (na v1, só "Dono") + data de entrada.
- **Sessão**: aparelho/navegador onde o Usuário está logado; data do último uso.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Criar conta e chegar ao Espaço vazio leva menos de 1 minuto.
- **SC-002**: Em uso normal, a pessoa digita a senha no celular no máximo uma vez a cada 90 dias.
- **SC-003**: 100% dos testes automatizados de isolamento (um por tipo de dado e por tipo de acesso: lista, detalhe, busca, sugestão, relatório) passam.
- **SC-004**: Nenhuma resposta do sistema permite descobrir se um e-mail tem conta.

## Assumptions

- Login apenas por e-mail e senha na v1 (sem login com Google/Apple, sem PIN, sem biometria).
- Exclusão de conta, exportação de dados pessoais (LGPD) e a interface de compartilhamento de Espaço (convites, papéis além de Dono, troca de Espaço) ficam para versões futuras. A v1 já deixa o modelo pronto para isso (FR-005, FR-005a), mas no uso real cada Espaço tem uma única pessoa.
- O sistema dispõe de um serviço de envio de e-mails.
- Não há perfil de administrador com acesso aos dados financeiros dos Espaços.
