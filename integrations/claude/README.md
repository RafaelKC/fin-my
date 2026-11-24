# Integração com o Claude (008)

Conector MCP + skill que deixam a pessoa lançar e consultar o Fin-My conversando com o Claude (web,
desktop e celular). O Claude age como o usuário que autorizou, no Espaço dele, com as mesmas regras do
app (`docs/arquitetura.md` §12).

| Arquivo | Para quê |
|---|---|
| [`skill/SKILL.md`](skill/SKILL.md) | Instruções para o Claude (a skill "fin-my") |
| [`roteiro-de-testes.md`](roteiro-de-testes.md) | Os 30 pedidos executados à mão antes de liberar (SC-002/SC-003) |

## 1. Publicar com HTTPS

O conector personalizado do Claude **só aceita endereço HTTPS** público. Em `localhost` ele não funciona.

1. Suba a stack (`docker-compose.yml`) em um servidor; só o `web` é publicado e ele já faz proxy de
   `/api` e `/mcp` para a `api` (`docs/arquitetura.md` §13).
2. Coloque HTTPS na frente do `web` (proxy reverso do servidor ou Cloudflare), com certificado válido.
3. No `.env` de produção, defina `APP_URL` com o endereço público **com `https://`** e sem barra no
   final (ex.: `https://fin-my.exemplo.com.br`). Troque também `Jwt__SigningKey`, a senha do banco e o SMTP.
4. Reinicie a stack e confira `https://<APP_URL>/health` → `200`.
5. Confira o endpoint do conector: `GET https://<APP_URL>/mcp` sem login deve responder `401` com
   o cabeçalho `WWW-Authenticate` (é assim que o Claude descobre o login). E
   `https://<APP_URL>/.well-known/oauth-protected-resource` deve responder `200` com JSON.
6. Os endereços de retorno do Claude aceitos no registro dinâmico são, por padrão,
   `https://claude.ai/api/mcp/auth_callback` e `https://claude.com/api/mcp/auth_callback`. Para mudar,
   defina `Mcp__AllowedRedirectUris__0`, `Mcp__AllowedRedirectUris__1`... (a lista substitui a padrão).

## 2. Adicionar o conector no Claude

1. No Claude, abra **Configurações → Conectores** (em planos Team/Enterprise, quem administra adiciona
   primeiro em **Configurações da organização → Conectores**).
2. **Adicionar conector personalizado**. Nome: `Fin-My`. URL: `<APP_URL>/mcp`
   (ex.: `https://fin-my.exemplo.com.br/mcp`). Não preencha client id/secret: o Claude se registra sozinho.
3. **Conectar**. O Claude abre a tela de login do Fin-My; entre com a conta da pessoa.
4. Na tela de consentimento, confira o texto "O Claude poderá ver e registrar seus lançamentos no
   Espaço <nome>" e clique em **Autorizar**.
5. Em um chat novo, ative o conector Fin-My (menu de ferramentas) e teste: "qual o saldo das minhas carteiras?".

Para ver ou revogar a conexão: no Fin-My, **Perfil → Aplicativos conectados** → *Desconectar*. Depois
de desconectar (ou de redefinir a senha), o próximo pedido no Claude falha em até 1 minuto e a pessoa
precisa autorizar de novo.

## 3. Enviar a skill

A skill é a pasta [`skill/`](skill/) (arquivo `SKILL.md`). Ela é versionada aqui e distribuída
manualmente.

1. Gere o pacote: `cd integrations/claude && zip -r fin-my-skill.zip skill` (o `.zip` deve conter a
   pasta `skill/` com o `SKILL.md` dentro; o nome da skill vem do campo `name: fin-my`). Não versione o `.zip`.
2. No Claude: **Configurações → Capacidades → Skills → Enviar skill** (ou **Customize → Skills**), escolha o
   `.zip` e ative a skill **fin-my**.
3. Sempre que o `SKILL.md` mudar, envie o pacote de novo (substitui a versão anterior).

## 4. Validar antes de liberar

1. Rode o [`roteiro-de-testes.md`](roteiro-de-testes.md) com a conta de teste, marque cada pedido e
   registre o resultado no PR/issue de liberação.
2. Confirme que desconectar no Perfil bloqueia o Claude em até 1 minuto (último item do roteiro).

## Ferramentas do conector

`list_catalog`, `get_wallet_balances`, `get_month_summary`, `list_pending`, `list_invoices`,
`get_invoice`, `get_settlements`, `search_entries`, `create_entry`, `create_card_purchase`,
`create_transfer`, `undo_creation`, `set_entry_status`, `pay_invoice`, `generate_settlement_charge`,
`delete_entry`. Contrato completo em
[`specs/008-assistente-claude/contracts/mcp-tools.md`](../../specs/008-assistente-claude/contracts/mcp-tools.md).

## Problemas comuns

| Sintoma | Causa provável |
|---|---|
| "Não foi possível conectar" ao adicionar | URL sem HTTPS/certificado inválido, ou `/mcp` não chega na `api` (proxy) |
| Login abre mas volta com erro de redirecionamento | `Mcp__AllowedRedirectUris` não inclui o retorno que o Claude usou |
| Claude diz que não tem acesso | Conexão revogada ou senha redefinida: autorize de novo |
| Respostas sem seguir o jeito Fin-My | Skill não enviada ou desativada |
