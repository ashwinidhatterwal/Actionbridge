plugins { id("com.android.application") }
android {
    namespace = "app.actionbridge"
    compileSdk = 36
    defaultConfig { applicationId = "app.actionbridge"; minSdk = 26; targetSdk = 36; versionCode = 70; versionName = "0.7.0"; buildConfigField("String", "SUPPORT_EMAIL", "\"${System.getenv("SUPPORT_EMAIL") ?: ""}\"") }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    buildFeatures { buildConfig = true }
    testOptions { unitTests.isReturnDefaultValues = true; unitTests.isIncludeAndroidResources = true }
    signingConfigs {
        create("publish") {
            val keyPath=System.getenv("UPLOAD_KEYSTORE_PATH")
            if(!keyPath.isNullOrBlank()) {
                storeFile=file(keyPath);storePassword=System.getenv("UPLOAD_STORE_PASSWORD")
                keyAlias=System.getenv("UPLOAD_KEY_ALIAS");keyPassword=System.getenv("UPLOAD_KEY_PASSWORD")
            }
        }
    }
    buildTypes { release { isMinifyEnabled = false; signingConfig=signingConfigs.getByName("publish") } }
}
kotlin { jvmToolchain(17) }
dependencies {
    implementation("androidx.webkit:webkit:1.14.0")
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
    implementation("androidx.work:work-runtime-ktx:2.10.1")
    implementation("com.google.guava:guava:33.3.1-android")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
    testImplementation("org.robolectric:robolectric:4.15.1")
}
// Keep the transitive AndroidX families on a consistent generation.
configurations.configureEach {
    resolutionStrategy.eachDependency {
        when {
            requested.group == "androidx.lifecycle" -> useVersion("2.10.0")
            requested.group == "androidx.core" && requested.name in listOf("core", "core-ktx") -> useVersion("1.18.0")
            requested.group == "androidx.concurrent" -> useVersion("1.2.0")
            requested.group == "androidx.annotation" && requested.name != "annotation-experimental" -> useVersion("1.9.1")
            requested.group == "androidx.annotation" && requested.name == "annotation-experimental" -> useVersion("1.4.1")
            requested.group == "androidx.collection" -> useVersion("1.5.0")
            requested.group == "com.google.errorprone" -> useVersion("2.36.0")
        }
    }
}
