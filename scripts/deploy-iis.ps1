<#
  deploy-iis.ps1
  Publishes the API and hosts it on Windows IIS (app pool + site) for the demo/viva.
  Run from an elevated (Administrator) PowerShell. No secret is stored in the repository:
  the MongoDB connection string and JWT key are passed as parameters and written only to
  the IIS app-pool environment.

  Example:
    powershell -ExecutionPolicy Bypass -File scripts\deploy-iis.ps1 `
      -MongoConnectionString "mongodb://localhost:27019/?replicaSet=rs0&directConnection=true" `
      -JwtKey "<at least 32 random characters>" -Port 8080
#>
param(
    [Parameter(Mandatory = $true)][string]$MongoConnectionString,
    [Parameter(Mandatory = $true)][ValidateLength(32, 256)][string]$JwtKey,
    [string]$DatabaseName = 'SolarGridExchangeDb',
    [string]$SiteName = 'SolarGridApi',
    [string]$PhysicalPath = 'C:\inetpub\SolarGrid\current',
    [int]$Port = 8080,
    [string[]]$CorsOrigins = @('http://localhost:5173'),
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated (Administrator) PowerShell.'
}

$appcmd = Join-Path $env:windir 'system32\inetsrv\appcmd.exe'
if (-not (Test-Path $appcmd)) {
    throw "IIS is not installed. Run: Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole,IIS-WebServer,IIS-ManagementConsole -All, then install the .NET 10 Hosting Bundle and run this script again."
}
if (-not (Test-Path 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2')) {
    throw 'The ASP.NET Core Module V2 is missing. Install the .NET 10 Hosting Bundle, run iisreset, then retry.'
}

if (-not $SkipPublish) {
    Push-Location $repositoryRoot
    try {
        dotnet publish SolarMicrogrid.API/SolarMicrogrid.API.csproj --configuration Release -p:PublishProfile=IIS-Release
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    }
    finally { Pop-Location }
}

$publishDir = Join-Path $repositoryRoot 'artifacts\iis\api'
if (-not (Test-Path (Join-Path $publishDir 'SolarMicrogrid.API.dll'))) {
    throw "Publish output not found in $publishDir."
}

New-Item -ItemType Directory -Force -Path $PhysicalPath | Out-Null
robocopy $publishDir $PhysicalPath /MIR /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Copying the published files failed.' }

Import-Module WebAdministration
if (-not (Test-Path "IIS:\AppPools\$SiteName")) { New-WebAppPool -Name $SiteName | Out-Null }
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedPipelineMode -Value Integrated
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name enable32BitAppOnWin64 -Value $false

if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
New-Website -Name $SiteName -ApplicationPool $SiteName -PhysicalPath $PhysicalPath -Port $Port | Out-Null

$environment = [ordered]@{
    ASPNETCORE_ENVIRONMENT          = 'Production'
    MongoSettings__ConnectionString = $MongoConnectionString
    MongoSettings__DatabaseName     = $DatabaseName
    JwtSettings__Key                = $JwtKey
    OpenApi__Enabled                = 'false'
}
for ($index = 0; $index -lt $CorsOrigins.Count; $index++) {
    $environment["Cors__AllowedOrigins__$index"] = $CorsOrigins[$index]
}

& $appcmd set apppool "/apppool.name:$SiteName" "/-environmentVariables" 2>$null | Out-Null
foreach ($entry in $environment.GetEnumerator()) {
    & $appcmd set apppool "/apppool.name:$SiteName" "/+environmentVariables.[name='$($entry.Key)',value='$($entry.Value)']" | Out-Null
}

icacls $PhysicalPath /grant "IIS AppPool\${SiteName}:(OI)(CI)RX" /T | Out-Null
if (-not (Get-NetFirewallRule -DisplayName "SolarGrid API $Port" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "SolarGrid API $Port" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
}

Restart-WebAppPool -Name $SiteName
Start-Sleep -Seconds 5

$health = Invoke-WebRequest -Uri "http://localhost:$Port/health" -UseBasicParsing
if ($health.StatusCode -ne 200) { throw "Health check returned $($health.StatusCode)." }
Write-Output "SolarGrid API is running on IIS: http://localhost:$Port/  (health: $($health.StatusCode))"
Write-Output "Android debug URL: http://<this-PC-LAN-IP>:$Port/api/   (set SOLARGRID_API_BASE_URL in SolarGridAndroid/local.properties)"
