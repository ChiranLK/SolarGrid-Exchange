# Prepare an Android device for SolarGrid debug login.
# USB (default): adb reverse + http://127.0.0.1:5076/api/
# WiFi (-UseWiFi): http://<PC-LAN-IP>:5076/api/ — no USB/adb required

param(
    [switch] $UseWiFi
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$androidDir = Join-Path $repoRoot 'SolarGridAndroid'
$localProperties = Join-Path $androidDir 'local.properties'
$apiProject = Join-Path $repoRoot 'SolarMicrogrid.API\SolarMicrogrid.API.csproj'
$adb = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
$apkPath = Join-Path $androidDir 'app\build\outputs\apk\debug\app-debug.apk'

function Get-LanIPv4 {
    $addresses = Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True' |
        ForEach-Object { $_.IPAddress } |
        Where-Object { $_ -match '^192\.168\.\d+\.\d+$' }
    return @($addresses | Select-Object -First 1)
}

function Set-LocalPropertiesApiUrl([string] $apiUrl) {
    if (-not (Test-Path $localProperties)) {
        throw "Missing $localProperties"
    }
    $lines = Get-Content $localProperties
    $found = $false
    $updated = foreach ($line in $lines) {
        if ($line -match '^\s*SOLARGRID_API_BASE_URL\s*=') {
            $found = $true
            "SOLARGRID_API_BASE_URL=$apiUrl"
        } else {
            $line
        }
    }
    if (-not $found) {
        $updated = @($updated) + "SOLARGRID_API_BASE_URL=$apiUrl"
    }
    [System.IO.File]::WriteAllLines($localProperties, $updated)
}

function Show-AdbHelp {
    Write-Host ''
    Write-Host 'Realme USB stuck on offline? Try on the phone:'
    Write-Host '  - Developer options -> Revoke USB debugging authorizations'
    Write-Host '  - Default USB configuration -> File transfer / MTP'
    Write-Host '  - USB debugging OFF, then ON again; replug cable'
    Write-Host '  - Unlock screen and tap Allow on the debugging prompt'
    Write-Host ''
    Write-Host 'Or skip USB entirely (recommended now):'
    Write-Host '  powershell -File scripts\prepare-android-device.ps1 -UseWiFi'
}

try {
    $health = Invoke-WebRequest -Uri 'http://127.0.0.1:5076/health' -UseBasicParsing -TimeoutSec 5
    if ($health.StatusCode -ne 200) {
        throw 'SolarGrid API health check failed.'
    }
} catch {
    throw @"
API is not running on port 5076.

Start it from ANY folder with:
  dotnet run --project "$apiProject" --launch-profile http

Leave that window open, then run this script again.
"@
}

$authorized = @()
if (Test-Path $adb) {
    $rawDevices = & $adb devices
    $authorized = @($rawDevices | Select-String '\tdevice$')
    if (@($rawDevices | Select-String '\toffline$').Count -gt 0) {
        Write-Host 'ADB sees the phone as offline (USB driver/debug issue). Continuing without USB...'
    }
}

if ($UseWiFi) {
    $lanIp = Get-LanIPv4
    if (-not $lanIp) {
        throw 'Could not detect a 192.168.x.x Wi-Fi address on this PC.'
    }
    $apiUrl = "http://${lanIp}:5076/api/"
    Set-LocalPropertiesApiUrl $apiUrl
    Write-Host "Wi-Fi mode: API URL $apiUrl"
    Write-Host 'Phone and PC must use the same Wi-Fi network.'
    Write-Host ''
    Write-Host 'If login still fails, run PowerShell as Administrator ONCE:'
    Write-Host '  netsh advfirewall firewall add rule name="SolarGrid API Dev 5076" dir=in action=allow protocol=TCP localport=5076'
} else {
    if ($authorized.Count -eq 0) {
        Show-AdbHelp
        throw 'No authorized USB device. Run with -UseWiFi instead.'
    }
    & $adb reverse --remove-all 2>$null
    & $adb reverse tcp:5076 tcp:5076
    Write-Host 'USB mode: adb reverse tcp:5076 tcp:5076'
    Set-LocalPropertiesApiUrl 'http://127.0.0.1:5076/api/'
}

Push-Location $androidDir
try {
    & .\gradlew.bat assembleDebug
    if ($authorized.Count -gt 0) {
        & .\gradlew.bat installDebug
        Write-Host 'Installed via USB/adb.'
    }
} finally {
    Pop-Location
}

Write-Host ''
if ($authorized.Count -eq 0) {
    Write-Host 'Install the APK manually on your phone:'
    Write-Host "  $apkPath"
    Write-Host 'Copy it to the phone (USB file transfer, WhatsApp, etc.) and open it to install.'
    Write-Host 'You may need to allow Install unknown apps for your file app.'
}
Write-Host ''
Write-Host 'Then open SolarGrid Exchange and sign in with a Prosumer account from your local database.'
