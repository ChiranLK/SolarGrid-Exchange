# Start Pixel_9 emulator, install debug APK (10.0.2.2 API), set GPS, open app for map testing.
# Requires: API running on http://localhost:5076, Android SDK with Pixel_9 AVD.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$androidDir = Join-Path $repoRoot 'SolarGridAndroid'
$apiProject = Join-Path $repoRoot 'SolarMicrogrid.API\SolarMicrogrid.API.csproj'
$localProperties = Join-Path $androidDir 'local.properties'
$adb = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
$emu = Join-Path $env:LOCALAPPDATA 'Android\Sdk\emulator\emulator.exe'
$avd = 'Pixel_9'

if (-not (Test-Path $adb)) { throw "adb not found at $adb" }
if (-not (Test-Path $emu)) { throw "emulator not found at $emu" }

try {
    $null = Invoke-WebRequest -Uri 'http://127.0.0.1:5076/health' -UseBasicParsing -TimeoutSec 5
} catch {
    throw @"
Start the API first (leave the window open):
  dotnet run --project "$apiProject" --launch-profile http
"@
}

# Emulator API URL (host PC from Android emulator)
$lines = Get-Content $localProperties
$updated = foreach ($line in $lines) {
    if ($line -match '^\s*SOLARGRID_API_BASE_URL\s*=') { 'SOLARGRID_API_BASE_URL=http://10.0.2.2:5076/api/' } else { $line }
}
if (-not ($updated -match 'SOLARGRID_API_BASE_URL=')) {
    $updated = @($updated) + 'SOLARGRID_API_BASE_URL=http://10.0.2.2:5076/api/'
}
[System.IO.File]::WriteAllText($localProperties, (($updated -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))

$devices = & $adb devices | Select-String 'emulator-\d+\s+device'
if (-not $devices) {
    Write-Host "Starting $avd emulator..."
    Start-Process -FilePath $emu -ArgumentList '-avd', $avd
    $deadline = (Get-Date).AddMinutes(5)
    do {
        Start-Sleep -Seconds 5
        $devices = & $adb devices | Select-String 'emulator-\d+\s+device'
    } while (-not $devices -and (Get-Date) -lt $deadline)
    if (-not $devices) { throw 'Emulator did not become ready in time.' }
    & $adb wait-for-device shell 'while [[ -z $(getprop sys.boot_completed) ]]; do sleep 1; done'
}

$serial = ($devices[0] -split '\s+')[0]
Write-Host "Using emulator $serial"

& $adb -s $serial emu geo fix 79.8612 6.9271
Push-Location $androidDir
try {
    & .\gradlew.bat installDebug
} finally {
    Pop-Location
}

& $adb -s $serial shell pm grant com.solargrid.exchange.debug android.permission.ACCESS_FINE_LOCATION 2>$null
& $adb -s $serial shell pm grant com.solargrid.exchange.debug android.permission.ACCESS_COARSE_LOCATION 2>$null
& $adb -s $serial shell monkey -p com.solargrid.exchange.debug -c android.intent.category.LAUNCHER 1 | Out-Null

Write-Host ''
Write-Host 'Emulator is ready. In the emulator window:'
Write-Host '  1. Skip onboarding if shown'
Write-Host '  2. Sign in with a Prosumer account from your local database'
Write-Host '  3. Bottom tab "Stations" -> map + nearby list'
Write-Host ''
Write-Host 'Maps key is loaded from local.properties (gitignored).'
