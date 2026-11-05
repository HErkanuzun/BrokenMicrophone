# Broken Microphone Android uygulamasını (APK) derler. Gradle gerekmez: JDK 17 + Android SDK (build-tools, platform) yeter.
#
#   powershell -ExecutionPolicy Bypass -File android\build.ps1
#
# İmza: BM_KEYSTORE (dosya yolu ya da base64) + BM_KEYSTORE_PASS ortam değişkenleri verilirse o anahtarla,
# yoksa obj\android\debug.keystore ile (uyarı verir; böyle imzalanan sürüm yayın sürümünün üstüne kurulamaz).
param(
    [string]$Sdk,
    [string]$Keystore = $env:BM_KEYSTORE,
    [string]$KeystorePass = $env:BM_KEYSTORE_PASS
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here

function Run([string]$exe) {
    & $exe @args
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($exe)) failed ($LASTEXITCODE)" }
}

# ---------------------------------------------------------------- sürüm (masaüstü uygulamasıyla aynı)
$ver = [regex]::Match((Get-Content "$root\src\AssemblyInfo.cs" -Raw), 'Version = "([^"]+)"').Groups[1].Value
$p = $ver.Split('.')
$code = [int]$p[0] * 1000 + $(if ($p.Length -gt 1) { [int]$p[1] * 10 } else { 0 }) + $(if ($p.Length -gt 2) { [int]$p[2] } else { 0 })

# ---------------------------------------------------------------- araçlar
if (-not $Sdk) {
    $Sdk = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "$env:LOCALAPPDATA\Android\Sdk") |
        Where-Object { $_ -and (Test-Path "$_\build-tools") } | Select-Object -First 1
}
if (-not $Sdk) { throw 'Android SDK bulunamadı (ANDROID_HOME).' }
$bt = Get-ChildItem "$Sdk\build-tools" -Directory | Where-Object Name -match '^\d+\.\d+\.\d+$' |
    Sort-Object { [version]$_.Name } | Select-Object -Last 1
$plat = Get-ChildItem "$Sdk\platforms" -Directory | Where-Object Name -match '^android-\d+$' |
    Sort-Object { [int]($_.Name -replace 'android-', '') } | Select-Object -Last 1
if (-not $bt -or -not $plat) { throw 'build-tools ya da platform eksik (sdkmanager "build-tools;35.0.0" "platforms;android-35").' }
$jar = Join-Path $plat.FullName 'android.jar'
$B = $bt.FullName

$jdk = $env:JAVA_HOME
if (-not $jdk -or -not (Test-Path "$jdk\bin\javac.exe")) {
    $jdk = Get-ChildItem "$env:LOCALAPPDATA\Android\jdk" -Directory -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $jdk) { throw 'JDK 17 bulunamadı (JAVA_HOME).' }
$env:JAVA_HOME = $jdk
$env:PATH = "$jdk\bin;$env:PATH"
Write-Host "SDK $($bt.Name) / $($plat.Name), JDK $jdk, version $ver ($code)"

# ---------------------------------------------------------------- QR okuma kütüphanesi (ZXing core, Apache 2.0)
# Maven Central'dan iner; SHA-256 sabit, değişmiş dosya kullanılmaz.
$zxVer = '3.5.3'
$zxSha = '8D8064C1636FDAEF7189DD9055C7D59950A8940A12F2293956446EC3C109FD82'
$deps = Join-Path $root 'obj\deps'
$zxing = Join-Path $deps "zxing-core-$zxVer.jar"
New-Item -ItemType Directory -Force $deps | Out-Null
if (-not (Test-Path $zxing) -or (Get-FileHash $zxing -Algorithm SHA256).Hash -ne $zxSha) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing "https://repo1.maven.org/maven2/com/google/zxing/core/$zxVer/core-$zxVer.jar" -OutFile "$zxing.part"
    if ((Get-FileHash "$zxing.part" -Algorithm SHA256).Hash -ne $zxSha) { Remove-Item "$zxing.part"; throw 'ZXing dosyasının SHA-256 özeti tutmuyor.' }
    Move-Item -Force "$zxing.part" $zxing
}

# ---------------------------------------------------------------- derle
$obj = Join-Path $root 'obj\android'
if (Test-Path $obj) { Get-ChildItem $obj -Exclude 'debug.keystore' | Remove-Item -Recurse -Force }
New-Item -ItemType Directory -Force "$obj\gen", "$obj\classes" | Out-Null

Run "$B\aapt2.exe" compile --dir "$here\res" -o "$obj\res.zip"
Run "$B\aapt2.exe" link -I $jar --manifest "$here\AndroidManifest.xml" `
    --min-sdk-version 24 --target-sdk-version 35 --version-code $code --version-name $ver `
    --java "$obj\gen" -o "$obj\base.apk" "$obj\res.zip"

$pkgDir = "$obj\gen\io\github\herkanuzun\brokenmicrophone"
[IO.File]::WriteAllText("$pkgDir\BuildInfo.java",
    "package io.github.herkanuzun.brokenmicrophone;`nfinal class BuildInfo { static final String VERSION = `"$ver`"; }`n")

$sources = @(Get-ChildItem "$here\java", "$obj\gen" -Recurse -Filter *.java | ForEach-Object FullName)
Run "$jdk\bin\javac.exe" -encoding UTF-8 -source 8 -target 8 -classpath "$jar;$zxing" '-Xlint:-options' `
    -d "$obj\classes" @sources
$classes = @(Get-ChildItem "$obj\classes" -Recurse -Filter *.class | ForEach-Object FullName)
# R8: dex'e çevirir ve kullanılmayan kodu atar (ZXing'in QR dışındaki kısımları)
Run "$jdk\bin\java.exe" -cp "$B\lib\d8.jar" com.android.tools.r8.R8 --release --min-api 24 --lib $jar `
    --pg-conf "$here\shrink.pro" --output $obj @classes $zxing

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open("$obj\base.apk", 'Update')
try { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$obj\classes.dex", 'classes.dex') }
finally { $zip.Dispose() }
Run "$B\zipalign.exe" -p -f 4 "$obj\base.apk" "$obj\aligned.apk"

# ---------------------------------------------------------------- imza
if ($Keystore -and -not (Test-Path $Keystore)) {
    # CI: gizli değişkendeki base64 anahtar dosyası
    $ksFile = "$obj\release.keystore"
    [IO.File]::WriteAllBytes($ksFile, [Convert]::FromBase64String($Keystore))
    $Keystore = $ksFile
}
if (-not $Keystore) {
    Write-Warning 'Yayın anahtarı yok: debug anahtarıyla imzalanıyor.'
    $Keystore = "$obj\debug.keystore"
    $KeystorePass = 'android'
    if (-not (Test-Path $Keystore)) {
        Run "$jdk\bin\keytool.exe" -genkeypair -keystore $Keystore -storepass android -keypass android -alias debug `
            -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=Broken Microphone Debug'
    }
}
New-Item -ItemType Directory -Force "$root\bin" | Out-Null
$apk = "$root\bin\BrokenMicrophone.apk"
Run "$B\apksigner.bat" sign --ks $Keystore --ks-pass "pass:$KeystorePass" --key-pass "pass:$KeystorePass" --out $apk "$obj\aligned.apk"
Remove-Item "$root\bin\BrokenMicrophone.apk.idsig" -ErrorAction SilentlyContinue
if ($ksFile) { Remove-Item $ksFile -Force }
Write-Host "OK  $apk  ($([math]::Round((Get-Item $apk).Length / 1KB)) KB)"
