<#
  seed-demo-data.ps1
  Seeds demo stations, energy slots and a Grid Operator through the running API, so a fresh
  database is demo-ready. Safe to run repeatedly: existing stations/accounts are reused and
  slots that already exist are skipped. Needs an existing Backoffice account.

  Example:
    powershell -ExecutionPolicy Bypass -File scripts\seed-demo-data.ps1 `
      -BackofficeEmail "<backoffice-email>" -BackofficePassword "<password>" -OperatorPassword "<new-operator-password>"
#>
param(
    [string]$ApiBase = 'http://127.0.0.1:5076/api',
    [Parameter(Mandatory = $true)][string]$BackofficeEmail,
    [Parameter(Mandatory = $true)][string]$BackofficePassword,
    [Parameter(Mandatory = $true)][string]$OperatorPassword,
    [int]$Days = 6
)

$ErrorActionPreference = 'Stop'

function Get-ErrorText($failure) {
    try {
        $reader = New-Object IO.StreamReader($failure.Exception.Response.GetResponseStream())
        return $reader.ReadToEnd()
    }
    catch { return $failure.Exception.Message }
}

$login = Invoke-RestMethod -Method Post -Uri "$ApiBase/auth/login" -ContentType 'application/json' `
    -Body (@{ email = $BackofficeEmail; password = $BackofficePassword } | ConvertTo-Json)
$token = if ($login.token) { $login.token } else { $login.accessToken }
$headers = @{ Authorization = "Bearer $token" }

$openEveryDay = 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday' |
    ForEach-Object { @{ dayOfWeek = $_; isOpen = $true; openingTime = '06:00'; closingTime = '20:00' } }

$demoStations = @(
    @{ name = 'Colombo Solar Hub'; description = 'Demo station in central Colombo'; address = 'Colombo Fort'
       latitude = 6.9271; longitude = 79.8612; energyGenerationCapacityKw = 250; batteryStorageCapacityKwh = 500; slotKwh = 50 },
    @{ name = 'Nugegoda Grid Node'; description = 'Second demo station'; address = 'Nugegoda'
       latitude = 6.8721; longitude = 79.8897; energyGenerationCapacityKw = 180; batteryStorageCapacityKwh = 320; slotKwh = 40 }
)

$existing = @(Invoke-RestMethod -Uri "$ApiBase/stations?page=1&pageSize=100" -Headers $headers)
$existingItems = if ($existing.Count -eq 1 -and $existing[0].items) { $existing[0].items } else { $existing }
$stationIds = @{}

foreach ($definition in $demoStations) {
    $match = $existingItems | Where-Object { $_.name -eq $definition.name } | Select-Object -First 1
    if ($match) {
        $stationIds[$definition.name] = $match.id
        Write-Output "Station exists: $($definition.name)"
        continue
    }

    $body = @{
        name = $definition.name; description = $definition.description; address = $definition.address
        latitude = $definition.latitude; longitude = $definition.longitude
        energyGenerationCapacityKw = $definition.energyGenerationCapacityKw
        batteryStorageCapacityKwh = $definition.batteryStorageCapacityKwh
        operatingSchedule = $openEveryDay
    } | ConvertTo-Json -Depth 5
    $created = Invoke-RestMethod -Method Post -Uri "$ApiBase/stations" -Headers $headers -ContentType 'application/json' -Body $body
    $stationIds[$definition.name] = $created.id
    Write-Output "Station created: $($definition.name)"
}

$slotsCreated = 0
foreach ($definition in $demoStations) {
    $stationId = $stationIds[$definition.name]
    foreach ($day in 1..$Days) {
        foreach ($localHour in 9, 14) {
            $start = [DateTime]::UtcNow.Date.AddDays($day).AddHours($localHour - 5.5)
            $body = @{
                stationId = $stationId
                startTimeUtc = $start.ToString('o')
                endTimeUtc = $start.AddHours(2).ToString('o')
                totalCapacityKwh = $definition.slotKwh
            } | ConvertTo-Json
            try {
                Invoke-RestMethod -Method Post -Uri "$ApiBase/stations/$stationId/slots" -Headers $headers -ContentType 'application/json' -Body $body | Out-Null
                $slotsCreated++
            }
            catch {
                $text = Get-ErrorText $_
                if ($text -notmatch 'overlap|already|exists') { Write-Warning "Slot skipped: $text" }
            }
        }
    }
}
Write-Output "Slots created: $slotsCreated"

$operatorEmail = 'demo.operator@solargrid.test'
$operators = @(Invoke-RestMethod -Uri "$ApiBase/users?role=GridOperator" -Headers $headers)
if ($operators | Where-Object { $_.email -eq $operatorEmail }) {
    Write-Output "Grid Operator exists: $operatorEmail"
}
else {
    $body = @{
        nic = '200000000010'; fullName = 'Demo Grid Operator'; email = $operatorEmail; phone = '0771234567'
        password = $OperatorPassword; role = 'GridOperator'; assignedStationId = $stationIds['Colombo Solar Hub']
    } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$ApiBase/users" -Headers $headers -ContentType 'application/json' -Body $body | Out-Null
    Write-Output "Grid Operator created: $operatorEmail (assigned to Colombo Solar Hub)"
}
