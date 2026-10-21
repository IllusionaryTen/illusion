plugins {
    id("com.android.application")
}

val illusionOrigin = providers.gradleProperty("illusionOrigin").orElse("https://illusion-messenger.illusion-messenger.workers.dev").get().trimEnd('/')
val illusionVersion = providers.gradleProperty("illusionVersion").orElse("0.8.2").get()
val illusionVersionCode = providers.gradleProperty("illusionVersionCode").orElse("802").get().toInt()

android {
    namespace = "com.illusion.messenger"
    compileSdk = 37

    defaultConfig {
        applicationId = "com.illusion.messenger"
        minSdk = 23
        targetSdk = 36
        versionCode = illusionVersionCode
        versionName = illusionVersion
        buildConfigField("String", "ILLUSION_ORIGIN", "\"$illusionOrigin\"")
        buildConfigField("String", "ILLUSION_START_URL", "\"$illusionOrigin/app/\"")
    }

    buildFeatures {
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }
}
