pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    // PREFER_SETTINGS: a biblioteca do Capacitor declara repositórios próprios.
    repositoriesMode.set(RepositoriesMode.PREFER_SETTINGS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "fin-my-android"
include(":app")
include(":wear")

// Capacitor (010): a biblioteca Android vem do npm do front (frontend/node_modules, `npm ci` antes do Gradle).
include(":capacitor-android")
project(":capacitor-android").projectDir = file("../frontend/node_modules/@capacitor/android/capacitor")
