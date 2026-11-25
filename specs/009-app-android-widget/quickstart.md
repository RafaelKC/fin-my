# Quickstart — 009

1. Stack local com `APP_URL` HTTPS e `Android__PackageName=br.app.finmy`, `Android__CertSha256__0=<SHA-256 da chave>`.
2. `curl -k <APP_URL>/.well-known/assetlinks.json` → JSON com o pacote e a impressão digital.
3. `cd android && ./gradlew assembleDebug -Pfinmy.appUrl=<APP_URL> [-Pfinmy.devCert=<pem>]`.
4. Instalar o APK (`adb install` ou copiar para o celular). Abrir o app: site em tela cheia; entrar.
5. Adicionar o widget "Fatura do cartão": entrar, autorizar ("App Fin-My (Android)"), escolher "Azul".
6. Conferir que Atual/Planejado/vencimento batem com `/cartoes/<id>/faturas/<mês>` (SC-002).
7. Perfil → Aplicativos conectados → desconectar o App Fin-My (Android) → tocar em atualizar no widget → "Entre de novo" (SC-003).
