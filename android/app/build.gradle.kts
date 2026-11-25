import java.net.URI
import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("org.jetbrains.kotlin.plugin.serialization")
}

// URL do site (propriedade Gradle `finmy.appUrl`). O padrão só serve para compilar.
val appUrl: String = (providers.gradleProperty("finmy.appUrl").orNull ?: "https://localhost:8443").trimEnd('/')
require(appUrl.startsWith("https://")) { "finmy.appUrl precisa começar com https:// (recebido: $appUrl)" }
require(URI(appUrl).host != null) { "finmy.appUrl sem host: $appUrl" }

// Certificado extra (PEM) aceito apenas na build debug; opcional e fora do repositório.
val devCertPath: String? = providers.gradleProperty("finmy.devCert").orNull?.takeIf { it.isNotBlank() }
if (devCertPath != null) {
    require(file(devCertPath).isFile) { "finmy.devCert não encontrado: $devCertPath" }
}

// Assinatura de release: android/keystore.properties (git-ignorado), com storeFile, storePassword,
// keyAlias e keyPassword. Sem o arquivo, a build release sai sem assinatura.
val keystorePropsFile = rootProject.file("keystore.properties")
val keystoreProps = Properties().apply {
    if (keystorePropsFile.isFile) keystorePropsFile.inputStream().use { load(it) }
}

val robolectricJars: Configuration by configurations.creating

android {
    namespace = "br.app.finmy"
    compileSdk = 36

    defaultConfig {
        // Fixo: uma futura troca de TWA por outra tecnologia mantém a instalação.
        applicationId = "br.app.finmy"
        minSdk = 26
        targetSdk = 36
        // versionCode sempre crescente: -Pfinmy.versionCode=N a cada release.
        versionCode = providers.gradleProperty("finmy.versionCode").orNull?.toInt() ?: 1
        versionName = providers.gradleProperty("finmy.versionName").orNull ?: "0.1.0"

        buildConfigField("String", "APP_URL", "\"$appUrl\"")
        manifestPlaceholders["appUrl"] = appUrl
        // Redirect do login do widget (br.app.finmy:/oauth/callback): o AppAuth declara a activity de retorno.
        manifestPlaceholders["appAuthRedirectScheme"] = "br.app.finmy"

        resValue("string", "shortcut_mes_url", "$appUrl/mes")
        resValue("string", "shortcut_painel_url", "$appUrl/painel")
        resValue(
            "string",
            "asset_statements",
            """[{"relation":["delegate_permission/common.handle_all_urls"],"target":{"namespace":"web","site":"$appUrl"}}]""",
        )
    }

    signingConfigs {
        if (keystorePropsFile.isFile) {
            create("release") {
                storeFile = rootProject.file(keystoreProps.getProperty("storeFile"))
                storePassword = keystoreProps.getProperty("storePassword")
                keyAlias = keystoreProps.getProperty("keyAlias")
                keyPassword = keystoreProps.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfigs.findByName("release")?.let { signingConfig = it }
        }
    }

    buildFeatures {
        buildConfig = true
        resValues = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_21
        targetCompatibility = JavaVersion.VERSION_21
    }

    testOptions {
        unitTests.isIncludeAndroidResources = true
        unitTests.all { test ->
            // Robolectric offline: o jar do Android vem do Gradle, sem download em tempo de teste.
            test.systemProperty("robolectric.offline", "true")
            test.systemProperty("robolectric.dependency.dir", robolectricJars.resolve().first().parent)
        }
    }

    lint {
        warningsAsErrors = true
        abortOnError = true
        // Versões/SDKs mais novos não são aviso de build (o resultado mudaria com a data, sem mudar o código).
        disable += setOf("GradleDependency", "NewerVersionAvailable", "AndroidGradlePluginVersion", "OldTargetApi")
    }
}

kotlin {
    jvmToolchain(21)
}

dependencies {
    implementation("com.google.androidbrowserhelper:androidbrowserhelper:2.6.2")
    implementation("androidx.browser:browser:1.9.0")

    // Widget (009 T004): Glance, WorkManager, login OAuth (AppAuth) e HTTP.
    implementation("androidx.glance:glance-appwidget:1.1.1")
    implementation("androidx.work:work-runtime-ktx:2.10.3")
    implementation("androidx.datastore:datastore-preferences:1.1.7")
    implementation("androidx.activity:activity-ktx:1.10.1")
    implementation("androidx.core:core-ktx:1.16.0")
    implementation("net.openid:appauth:0.11.1")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.9.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")

    testImplementation("junit:junit:4.13.2")
    testImplementation("com.squareup.okhttp3:mockwebserver:4.12.0")
    testImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-test:1.10.2")
    testImplementation("androidx.glance:glance-appwidget-testing:1.1.1")
    testImplementation("androidx.work:work-testing:2.10.3")
    testImplementation("org.robolectric:robolectric:4.16")
    testImplementation("androidx.test:core:1.7.0")
    // Deve casar com o SDK de @Config(sdk = [34]) no teste (Android 14).
    robolectricJars("org.robolectric:android-all-instrumented:14-robolectric-10818077-i7")
}

// network_security_config da build debug: aceita certificados instalados no aparelho (CA de usuário,
// como o da VPN) e, opcionalmente, o PEM de `finmy.devCert`. A release não tem esse arquivo.
abstract class GenerateDebugNetworkConfig : DefaultTask() {
    @get:Optional
    @get:InputFile
    @get:PathSensitive(PathSensitivity.NONE)
    abstract val devCert: RegularFileProperty

    @get:OutputDirectory
    abstract val outputDir: DirectoryProperty

    @TaskAction
    fun generate() {
        val out = outputDir.get().asFile
        out.deleteRecursively()
        val xmlDir = File(out, "xml").apply { mkdirs() }
        val hasCert = devCert.isPresent
        if (hasCert) {
            val rawDir = File(out, "raw").apply { mkdirs() }
            devCert.get().asFile.copyTo(File(rawDir, "finmy_dev_cert.pem"), overwrite = true)
        }
        val extra = if (hasCert) "\n            <certificates src=\"@raw/finmy_dev_cert\" />" else ""
        File(xmlDir, "network_security_config.xml").writeText(
            """<?xml version="1.0" encoding="utf-8"?>
<network-security-config>
    <base-config>
        <trust-anchors>
            <certificates src="system" />
            <certificates src="user" />$extra
        </trust-anchors>
    </base-config>
</network-security-config>
""",
        )
    }
}

androidComponents {
    onVariants(selector().withBuildType("debug")) { variant ->
        val task = tasks.register<GenerateDebugNetworkConfig>("generate${variant.name.replaceFirstChar { it.uppercase() }}NetworkConfig") {
            devCertPath?.let { devCert.set(file(it)) }
        }
        variant.sources.res?.addGeneratedSourceDirectory(task, GenerateDebugNetworkConfig::outputDir)
    }
}
