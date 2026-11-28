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
        versionCode = 7
        versionName = "1.6"
    }
    buildTypes {
        release {
            isMinifyEnabled = false
            // signed with the debug key so the APK installs right away (no store, no keystore needed)
            signingConfig = signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}
