# App Android (009)

APK do Fin-My com **Capacitor** (desde a 010; antes era uma TWA): a WebView do app carrega o próprio site
(`server.url` = `finmy.appUrl`), com o mesmo login por cookie, sem barra de endereço. O widget "Fatura do cartão"
usa a sessão do app: estando logado, o app conecta o widget sozinho (sem segundo login).
Sem Play Store: o APK é instalado à mão. O projeto fica em [`android/`](../../android) e usa o
pacote fixo **`br.app.finmy`**.

## 1. Pré-requisitos

- Java 21 e o Android SDK (variável `ANDROID_HOME`, plataforma 36).
- `npm ci` em `frontend/` antes do Gradle: a biblioteca Android do Capacitor vem de `frontend/node_modules`.
- O site publicado com **HTTPS** (`APP_URL`).

## 2. Compilar

```bash
cd android
./gradlew assembleDebug -Pfinmy.appUrl=https://fin-my.exemplo.com.br
```

O APK sai em `android/app/build/outputs/apk/debug/app-debug.apk`. Para conferir tudo como o CI:

```bash
./gradlew lint testDebugUnitTest assembleDebug
```

| Propriedade Gradle | Para quê |
|---|---|
| `finmy.appUrl` | Endereço do site, `https://...` sem barra final (padrão `https://localhost:8443`, só para compilar). |
| `finmy.devCert` | (opcional, só debug) caminho de um PEM extra aceito pelo app nativo. Fica fora do repositório. |
| `finmy.versionCode` / `finmy.versionName` | Versão do APK. O `versionCode` precisa **sempre crescer** a cada release. |

Podem ficar em `~/.gradle/gradle.properties` para não repetir na linha de comando.

## 3. Instalar o APK no celular

1. Copie `app-debug.apk` (ou o de release) para o celular, ou use `adb install -r app-debug.apk`.
2. Permita "instalar apps de fontes desconhecidas" para o app que abriu o arquivo.
3. Abra o "Fin-My". O site abre em tela cheia; entre com seu usuário.

Os atalhos (segurar o ícone) abrem **Mês atual** e **Painel**. O APK debug do CI fica como artifact
`fin-my-debug-apk` de cada execução do workflow.

## 4. Release assinado (local)

A chave **nunca** vai para o repositório. Guarde-a fora dele, por exemplo em
`~/.local/share/fin-my-android/`:

```bash
mkdir -p ~/.local/share/fin-my-android
keytool -genkeypair -v -keystore ~/.local/share/fin-my-android/finmy.jks \
  -alias finmy -keyalg RSA -keysize 2048 -validity 10000
```

Crie `android/keystore.properties` (já ignorado pelo git):

```properties
storeFile=/home/voce/.local/share/fin-my-android/finmy.jks
storePassword=...
keyAlias=finmy
keyPassword=...
```

```bash
./gradlew assembleRelease -Pfinmy.appUrl=https://fin-my.exemplo.com.br -Pfinmy.versionCode=2
```

Use **sempre a mesma chave** e um `versionCode` maior a cada release: assim o celular atualiza o app
em vez de recusar a instalação. A build release não inclui nenhum certificado extra. Sem
`keystore.properties`, ela sai sem assinatura (`app-release-unsigned.apk`).

## 5. Certificado da VPN (HTTPS autoassinado) no celular

Quando o servidor só é acessível pela VPN com certificado autoassinado, o app (WebView) e o
app nativo precisam confiar nele:

1. Exporte o certificado da CA (ou do servidor) em PEM/CRT e leve ao celular.
2. No Android: **Configurações → Segurança → Mais configurações de segurança → Criptografia e
   credenciais → Instalar um certificado → Certificado de CA** e escolha o arquivo (os nomes dos menus
   variam por fabricante). É preciso ter um bloqueio de tela configurado.
3. Abra o site no Chrome e confira que não há aviso de certificado.
4. (Só build debug, para o app nativo) o `network_security_config` já aceita os certificados de usuário
   instalados no passo 2. Se preferir embutir o PEM no APK debug, compile com
   `-Pfinmy.devCert=/caminho/ca.pem`.

> O app (WebView) e o widget respeitam o `network_security_config` da build debug, que aceita os certificados
> de usuário instalados no passo 2.

## 6. Widget "Fatura do cartão"

1. Abra o app e entre. Logado, o app conecta o widget: pede ao nativo um `code_challenge` (PKCE), cria o ticket de
   consentimento do cliente `finmy-android` com a sessão do app e o nativo troca o ticket por tokens
   (`/oauth/authorize` sem seguir o redirect e `/oauth/token`). Nenhum navegador é aberto.
2. Na tela inicial: segurar → Widgets → **Fatura do cartão** → escolher o cartão. Se o widget ainda não estiver
   conectado, a configuração mostra **Abrir o app**; ao voltar, lista os cartões.
3. Sair do app desconecta o widget. Também dá para desconectar em Perfil → Aplicativos conectados
   ("App Fin-My (Android)").
