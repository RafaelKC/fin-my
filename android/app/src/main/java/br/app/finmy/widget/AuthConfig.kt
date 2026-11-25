package br.app.finmy.widget

/** Parâmetros do cliente OAuth público do widget (specs/009 research R3). */
object AuthConfig {
    const val CLIENT_ID = "finmy-android"
    const val REDIRECT_URI = "br.app.finmy:/oauth/callback"
    const val AUTHORIZE_PATH = "/oauth/authorize"
    const val TOKEN_PATH = "/oauth/token"
}
