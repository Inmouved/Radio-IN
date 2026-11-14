plugins { id("com.android.application"); id("org.jetbrains.kotlin.android") }
android {
    namespace = "org.radioin.player"
    compileSdk = 36
    buildToolsVersion = "36.0.0"
    defaultConfig {
        applicationId = "org.radioin.player"
        minSdk = 26
        targetSdk = 36
        versionCode = 5
        versionName = "Bêta 1.3 Android"
        buildConfigField("String", "RELEASE_VERSION", "\"1.3.0\"")
    }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { buildConfig = true }
    buildTypes {
        getByName("debug") { applicationIdSuffix = ".test"; versionNameSuffix = "-test" }
        getByName("release") {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // Beta distribution key is supplied locally, never committed or put in the APK.
            signingConfig = signingConfigs.create("beta") {
                storeFile = file(System.getenv("RADIOIN_KEYSTORE") ?: "../../../private/RadioIN-Android-signature/radioin-beta.jks")
                storePassword = System.getenv("RADIOIN_STORE_PASSWORD")
                keyAlias = "radioin"
                keyPassword = System.getenv("RADIOIN_STORE_PASSWORD")
            }
        }
    }
    testOptions { unitTests.isIncludeAndroidResources = true }
    lint { abortOnError = true; disable += "SetTextI18n" }
}
dependencies {
    implementation("androidx.media3:media3-exoplayer:1.8.1")
    implementation("androidx.media3:media3-session:1.8.1")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.robolectric:robolectric:4.16.1")
}
