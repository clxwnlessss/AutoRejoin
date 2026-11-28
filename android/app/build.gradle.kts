plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.autorejoin.mobile"
    compileSdk = 34
    defaultConfig {
        applicationId = "com.autorejoin.mobile"
        minSdk = 24
        targetSdk = 34
        versionCode = 9
        versionName = "1.8"
    }
    // One permanent key (GitHub secret, decoded by the workflow): every version has the same signature, so updates
    // install over the old app and keep its accounts. Without the secret (local builds) the debug key is used.
    val keystore = System.getenv("ANDROID_KEYSTORE_FILE")
    signingConfigs {
        create("permanent") {
            if (keystore != null) {
                storeFile = file(keystore)
                storeType = "pkcs12"
                storePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ANDROID_KEY_ALIAS") ?: "autorejoin"
                keyPassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
            }
        }
    }
    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (keystore != null) signingConfigs.getByName("permanent") else signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}
