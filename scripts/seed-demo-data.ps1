<#
  seed-demo-data.ps1
  Loads realistic demo data through the running API so every role has something meaningful to show:

    * 7 energy stations across Sri Lanka (GPS location, kW generation, kWh battery storage,
      weekly operating schedule, bookable energy slots for the next 6 days, one station deactivated)
    * 2 Backoffice users (the first one must already exist, see docs/member-1/backoffice-bootstrap.md)
    * 4 Grid Operators, each assigned to a station
    * 9 Prosumers identified by NIC in different account states
      (Active, Pending activation, deactivation requested, Deactivated)
    * Reservations in every state: Pending, Approved, Rejected, Cancelled and Completed
      (Completed ones go through the real QR generate -> verify -> complete flow)

  Safe to run on a fresh database. Existing accounts/stations are reused, so re-running only fills gaps.
  No passwords are stored in this file; they are mandatory parameters.

  Example:
    powershell -ExecutionPolicy Bypass -File scripts\seed-demo-data.ps1 `
      -BackofficeEmail "<first-backoffice-email>" -BackofficePassword "<its-password>" `
      -DemoPassword "<password-for-all-other-demo-accounts>"
#>
param(
    [string]$ApiBase = 'http://127.0.0.1:5076/api',
    [Parameter(Mandatory = $true)][string]$BackofficeEmail,
    [Parameter(Mandatory = $true)][string]$BackofficePassword,
    [Parameter(Mandatory = $true)][string]$DemoPassword
)

$ErrorActionPreference = 'Stop'
$emailDomain = 'solargrid.example'
$colombo = [TimeSpan]::FromHours(5.5)

function Get-ErrorText($failure) {
    try {
        $reader = New-Object IO.StreamReader($failure.Exception.Response.GetResponseStream())
        return $reader.ReadToEnd()
    }
    catch { return $failure.Exception.Message }
}

function Invoke-Api([string]$Method, [string]$Path, $Body, $Headers, [switch]$Idempotent) {
    $request = @{ Method = $Method; Uri = "$ApiBase$Path"; ContentType = 'application/json' }
    $h = @{}
    if ($Headers) { $Headers.GetEnumerator() | ForEach-Object { $h[$_.Key] = $_.Value } }
    if ($Idempotent) { $h['Idempotency-Key'] = [guid]::NewGuid().ToString() }
    if ($h.Count) { $request.Headers = $h }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 6) }
    try { return Invoke-RestMethod @request }
    catch { throw "$Method $Path failed: $(Get-ErrorText $_)" }
}

function Get-Items($response) {
    if ($null -eq $response) { return @() }
    if ($response.PSObject.Properties.Name -contains 'items') { return @($response.items) }
    return @($response)
}

function Sign-In([string]$Email, [string]$Password) {
    $login = Invoke-Api 'Post' '/auth/login' @{ email = $Email; password = $Password }
    $token = if ($login.token) { $login.token } else { $login.accessToken }
    return @{ Authorization = "Bearer $token" }
}

$backoffice = Sign-In $BackofficeEmail $BackofficePassword

$week = 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'
function New-Schedule([string]$Weekday, [string]$Saturday, [string]$Sunday) {
    $week | ForEach-Object {
        $times = switch ($_) { 'Saturday' { $Saturday } 'Sunday' { $Sunday } default { $Weekday } }
        if ($times) {
            $open, $close = $times -split '-'
            @{ dayOfWeek = $_; isOpen = $true; openingTime = $open; closingTime = $close }
        }
        else { @{ dayOfWeek = $_; isOpen = $false; openingTime = $null; closingTime = $null } }
    }
}

# ---------------------------------------------------------------- Stations
$stationDefinitions = @(
    @{ key = 'colombo'; name = 'Colombo Solar Hub'; address = '42 Chatham Street, Colombo 01'
       description = 'Rooftop solar array with a large battery bank serving the Colombo Fort business district.'
       lat = 6.9344; lon = 79.8428; kw = 250; kwh = 500; slotKwh = 100; schedule = (New-Schedule '06:00-20:00' '07:00-18:00' '08:00-16:00') },
    @{ key = 'lavinia'; name = 'Mount Lavinia Coastal Node'; address = '118 Galle Road, Mount Lavinia'
       description = 'Coastal community microgrid node with sea-breeze cooled battery containers.'
       lat = 6.8389; lon = 79.8653; kw = 120; kwh = 240; slotKwh = 60; schedule = (New-Schedule '07:00-19:00' '08:00-16:00' $null) },
    @{ key = 'nugegoda'; name = 'Nugegoda Grid Node'; address = '25 High Level Road, Nugegoda'
       description = 'Suburban storage node balancing rooftop generation from nearby homes.'
       lat = 6.8649; lon = 79.8997; kw = 180; kwh = 360; slotKwh = 80; schedule = (New-Schedule '06:00-20:00' '07:00-18:00' '08:00-16:00') },
    @{ key = 'malabe'; name = 'Malabe Tech Park Station'; address = 'Athurugiriya Road, Malabe'
       description = 'High capacity station next to the technology park with fast discharge battery racks.'
       lat = 6.9061; lon = 79.9712; kw = 300; kwh = 600; slotKwh = 120; schedule = (New-Schedule '06:00-20:00' '07:00-18:00' '08:00-16:00') },
    @{ key = 'negombo'; name = 'Negombo Lagoon Station'; address = '77 Lewis Place, Negombo'
       description = 'Lagoon-side solar field with mid-size battery storage.'
       lat = 7.2083; lon = 79.8358; kw = 150; kwh = 300; slotKwh = 70; schedule = (New-Schedule '07:00-19:00' '08:00-17:00' '09:00-15:00') },
    @{ key = 'kandy'; name = 'Kandy Hills Microgrid'; address = '9 Peradeniya Road, Kandy'
       description = 'Hill-country microgrid combining solar generation and a community battery.'
       lat = 7.2906; lon = 80.6337; kw = 220; kwh = 440; slotKwh = 90; schedule = (New-Schedule '06:00-19:00' '07:00-17:00' $null) },
    @{ key = 'galle'; name = 'Galle Fort Energy Point'; address = '31 Church Street, Galle'
       description = 'Heritage-zone station currently offline for battery replacement.'
       lat = 6.0329; lon = 80.2168; kw = 200; kwh = 400; slotKwh = 80; schedule = (New-Schedule '07:00-18:00' '08:00-15:00' $null) }
)

$existing = Get-Items (Invoke-Api 'Get' '/stations?page=1&pageSize=100' $null $backoffice)
$stations = @{}
foreach ($definition in $stationDefinitions) {
    $match = $existing | Where-Object { $_.name -eq $definition.name } | Select-Object -First 1
    if ($match) { $stations[$definition.key] = $match.id; Write-Output "Station exists: $($definition.name)"; continue }
    $created = Invoke-Api 'Post' '/stations' @{
        name = $definition.name; description = $definition.description; address = $definition.address
        latitude = $definition.lat; longitude = $definition.lon
        energyGenerationCapacityKw = $definition.kw; batteryStorageCapacityKwh = $definition.kwh
        operatingSchedule = $definition.schedule
    } $backoffice
    $stations[$definition.key] = $created.id
    Write-Output "Station created: $($definition.name)"
}

# ---------------------------------------------------------------- Slots
$localToday = [DateTime]::UtcNow.Add($colombo).Date
$slotHours = 8, 10, 13, 15
$slotFactors = 1.0, 1.0, 0.8, 0.8
$slotsCreated = 0
foreach ($definition in $stationDefinitions) {
    $scheduleByDay = @{}
    $definition.schedule | ForEach-Object { $scheduleByDay[$_.dayOfWeek] = $_ }
    foreach ($day in 1..6) {
        $localDate = $localToday.AddDays($day)
        $dayRule = $scheduleByDay[$localDate.DayOfWeek.ToString()]
        if (-not $dayRule.isOpen) { continue }
        for ($i = 0; $i -lt $slotHours.Count; $i++) {
            $localStart = $localDate.AddHours($slotHours[$i])
            $opens = $localDate.Add([TimeSpan]::Parse($dayRule.openingTime))
            $closes = $localDate.Add([TimeSpan]::Parse($dayRule.closingTime))
            if ($localStart -lt $opens -or $localStart.AddHours(2) -gt $closes) { continue }
            $startUtc = [DateTime]::SpecifyKind($localStart.Subtract($colombo), 'Utc')
            try {
                Invoke-Api 'Post' "/stations/$($stations[$definition.key])/slots" @{
                    stationId = $stations[$definition.key]
                    startTimeUtc = $startUtc.ToString('o'); endTimeUtc = $startUtc.AddHours(2).ToString('o')
                    totalCapacityKwh = [math]::Round($definition.slotKwh * $slotFactors[$i])
                } $backoffice | Out-Null
                $slotsCreated++
            }
            catch { if ("$_" -notmatch 'overlap|already|exists') { Write-Warning "Slot skipped: $_" } }
        }
    }
}
Write-Output "Slots created: $slotsCreated"

# ---------------------------------------------------------------- Staff
$staff = @(
    @{ nic = '198575103421'; role = 'Backoffice'; name = 'Ruwan Jayasekara'; email = 'ruwan.jayasekara'; phone = '0712345678'; address = '12 Park Street, Colombo 02'; station = $null },
    @{ nic = '199036712458'; role = 'GridOperator'; name = 'Dilshan Fernando'; email = 'dilshan.fernando'; phone = '0772345679'; address = '5 Fort Road, Colombo 01'; station = 'colombo' },
    @{ nic = '198812345679'; role = 'GridOperator'; name = 'Isuru Madushanka'; email = 'isuru.madushanka'; phone = '0752345680'; address = '30 Beach Road, Mount Lavinia'; station = 'lavinia' },
    @{ nic = '199145678912'; role = 'GridOperator'; name = 'Tharindu Gunawardena'; email = 'tharindu.gunawardena'; phone = '0762345681'; address = '8 Pagoda Road, Nugegoda'; station = 'nugegoda' },
    @{ nic = '199467891234'; role = 'GridOperator'; name = 'Sachini Rajapaksha'; email = 'sachini.rajapaksha'; phone = '0702345682'; address = '21 Malabe Road, Malabe'; station = 'malabe' }
)
$existingUsers = Get-Items (Invoke-Api 'Get' '/users?page=1&pageSize=200' $null $backoffice)
foreach ($member in $staff) {
    $email = "$($member.email)@$emailDomain"
    if ($existingUsers | Where-Object { $_.email -eq $email }) { Write-Output "Account exists: $email"; continue }
    $body = @{ nic = $member.nic; fullName = $member.name; email = $email; phone = $member.phone
               address = $member.address; password = $DemoPassword; role = $member.role }
    if ($member.station) { $body.assignedStationId = $stations[$member.station] }
    Invoke-Api 'Post' '/users' $body $backoffice | Out-Null
    Write-Output "$($member.role) created: $($member.name)"
}

# ---------------------------------------------------------------- Prosumers
$prosumers = @(
    @{ key = 'kavindu'; nic = '200134502817'; name = 'Kavindu Senanayake'; phone = '0771234501'; address = '14 Temple Road, Mount Lavinia'; state = 'Active' },
    @{ key = 'hansika'; nic = '199678103452'; name = 'Hansika Jayawardena'; phone = '0712345602'; address = '27 High Level Road, Nugegoda'; state = 'Active' },
    @{ key = 'pradeep'; nic = '902345678V'; name = 'Pradeep Wijesinghe'; phone = '0763456703'; address = '9 Pelawatta Road, Battaramulla'; state = 'Active' },
    @{ key = 'ishara'; nic = '199852301764'; name = 'Ishara Dissanayake'; phone = '0754567804'; address = '56 Galle Road, Dehiwala'; state = 'Active' },
    @{ key = 'thilini'; nic = '200245608913'; name = 'Thilini Amarasinghe'; phone = '0725678905'; address = '3 Lake Drive, Kandy'; state = 'Active' },
    @{ key = 'nimal'; nic = '197634567890'; name = 'Nimal Karunaratne'; phone = '0746789006'; address = '18 Church Lane, Negombo'; state = 'DeactivationRequested' },
    @{ key = 'chamari'; nic = '198923456789'; name = 'Chamari Liyanage'; phone = '0787890107'; address = '62 Temple Road, Galle'; state = 'Deactivated' },
    @{ key = 'roshan'; nic = '881234567V'; name = 'Roshan Gamage'; phone = '0708901208'; address = '40 Station Road, Kottawa'; state = 'Pending' },
    @{ key = 'amaya'; nic = '200378945612'; name = 'Amaya Ratnayake'; phone = '0779012309'; address = '7 Orchard Avenue, Rajagiriya'; state = 'Pending' }
)
$existingProsumers = Get-Items (Invoke-Api 'Get' '/users?page=1&pageSize=200&role=Prosumer' $null $backoffice)
$sessions = @{}
foreach ($person in $prosumers) {
    $first = ($person.name -split ' ')[0].ToLower()
    $last = ($person.name -split ' ')[1].ToLower()
    $email = "$first.$last@$emailDomain"
    if (-not ($existingProsumers | Where-Object { $_.email -eq $email })) {
        Invoke-Api 'Post' '/auth/register' @{
            nic = $person.nic; fullName = $person.name; email = $email; phone = $person.phone
            address = $person.address; password = $DemoPassword
        } | Out-Null
        if ($person.state -ne 'Pending') {
            Invoke-Api 'Patch' "/users/$($person.nic)/activate" $null $backoffice | Out-Null
        }
        Write-Output "Prosumer created: $($person.name) ($($person.state))"
    }
    $person.email = $email
    if ($person.state -in 'Active', 'DeactivationRequested') { $sessions[$person.key] = Sign-In $email $DemoPassword }
}

# ---------------------------------------------------------------- Reservations
$operatorSessions = @{}
foreach ($member in $staff | Where-Object { $_.role -eq 'GridOperator' }) {
    $operatorSessions[$member.station] = Sign-In "$($member.email)@$emailDomain" $DemoPassword
}

$slotCache = @{}
function Get-Slot([string]$StationKey, [int]$Day, [int]$HourIndex) {
    if (-not $slotCache.ContainsKey($StationKey)) {
        $slotCache[$StationKey] = Get-Items (Invoke-Api 'Get' "/stations/$($stations[$StationKey])/slots?page=1&pageSize=100" $null $backoffice)
    }
    $startUtc = $localToday.AddDays($Day).AddHours($slotHours[$HourIndex]).Subtract($colombo)
    return $slotCache[$StationKey] | Where-Object { ([DateTimeOffset]$_.startTimeUtc).UtcDateTime.Ticks -eq $startUtc.Ticks } | Select-Object -First 1
}

$plan = @(
    @{ who = 'kavindu'; station = 'colombo'; day = 2; slot = 0; kwh = 30; outcome = 'Approved' },
    @{ who = 'kavindu'; station = 'malabe'; day = 4; slot = 2; kwh = 45; outcome = 'Pending' },
    @{ who = 'hansika'; station = 'nugegoda'; day = 2; slot = 1; kwh = 25; outcome = 'Approved' },
    @{ who = 'hansika'; station = 'colombo'; day = 3; slot = 3; kwh = 40; outcome = 'Pending' },
    @{ who = 'pradeep'; station = 'malabe'; day = 3; slot = 0; kwh = 60; outcome = 'Approved' },
    @{ who = 'pradeep'; station = 'colombo'; day = 5; slot = 1; kwh = 35; outcome = 'Rejected' },
    @{ who = 'pradeep'; station = 'kandy'; day = 4; slot = 0; kwh = 40; outcome = 'Pending' },
    @{ who = 'ishara'; station = 'lavinia'; day = 2; slot = 2; kwh = 20; outcome = 'Pending' },
    @{ who = 'ishara'; station = 'nugegoda'; day = 4; slot = 0; kwh = 30; outcome = 'Cancelled' },
    @{ who = 'thilini'; station = 'kandy'; day = 3; slot = 1; kwh = 50; outcome = 'Approved' },
    @{ who = 'thilini'; station = 'negombo'; day = 5; slot = 3; kwh = 25; outcome = 'Pending' },
    @{ who = 'hansika'; station = 'lavinia'; day = 1; slot = 0; kwh = 28; outcome = 'Completed' },
    @{ who = 'pradeep'; station = 'nugegoda'; day = 1; slot = 1; kwh = 32; outcome = 'Completed' },
    @{ who = 'ishara'; station = 'colombo'; day = 1; slot = 2; kwh = 22; outcome = 'Completed' }
)

$existingReservations = @{}
foreach ($key in $sessions.Keys) {
    $existingReservations[$key] = Get-Items (Invoke-Api 'Get' '/reservations?page=1&pageSize=100' $null $sessions[$key])
}

$counts = @{}
foreach ($item in $plan) {
    $slot = Get-Slot $item.station $item.day $item.slot
    if (-not $slot) { Write-Warning "No slot for $($item.station) day +$($item.day) #$($item.slot) (station closed that day)"; continue }
    if ($existingReservations[$item.who] | Where-Object { $_.slotId -eq $slot.id }) { continue }

    $who = $sessions[$item.who]
    try {
        $reservation = Invoke-Api 'Post' '/reservations' @{ slotId = $slot.id; requestedEnergyKwh = $item.kwh } $who -Idempotent
        switch ($item.outcome) {
            'Approved' {
                Invoke-Api 'Post' "/reservations/$($reservation.id)/approve" @{ expectedVersion = $reservation.version } $backoffice -Idempotent | Out-Null
            }
            'Rejected' {
                Invoke-Api 'Post' "/reservations/$($reservation.id)/reject" @{
                    expectedVersion = $reservation.version
                    reason = 'Requested energy is above the safe discharge limit for this time window.'
                } $backoffice -Idempotent | Out-Null
            }
            'Cancelled' {
                Invoke-Api 'Post' "/reservations/$($reservation.id)/cancel" @{
                    expectedVersion = $reservation.version; reason = 'Change of plans, no longer need the energy.'
                } $who -Idempotent | Out-Null
            }
            'Completed' {
                $approved = Invoke-Api 'Post' "/reservations/$($reservation.id)/approve" @{ expectedVersion = $reservation.version } $backoffice -Idempotent
                $qr = Invoke-Api 'Post' "/transactions/reservations/$($reservation.id)/qr" $null $who
                $operator = $operatorSessions[$item.station]
                $verification = Invoke-Api 'Post' '/transactions/verify' @{ token = $qr.qrToken } $operator
                Invoke-Api 'Post' "/transactions/reservations/$($reservation.id)/complete" @{
                    verificationId = $verification.verificationId; expectedVersion = $verification.reservationVersion
                } $operator | Out-Null
            }
        }
        $counts[$item.outcome] = 1 + [int]$counts[$item.outcome]
    }
    catch { Write-Warning "Reservation skipped ($($item.who) @ $($item.station)): $_" }
}
Write-Output ("Reservations: " + (($counts.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '))

# ---------------------------------------------------------------- Account / station states
$nimal = $prosumers | Where-Object { $_.key -eq 'nimal' }
try {
    Invoke-Api 'Post' '/prosumers/me/deactivation-request' $null $sessions['nimal'] | Out-Null
    Write-Output 'Deactivation request filed: Nimal Karunaratne'
}
catch { Write-Output "Deactivation request skipped: $_" }

$chamari = $prosumers | Where-Object { $_.key -eq 'chamari' }
try {
    Invoke-Api 'Patch' "/users/$($chamari.nic)/deactivate" $null $backoffice | Out-Null
    Write-Output 'Prosumer deactivated: Chamari Liyanage'
}
catch { Write-Output "Deactivate skipped: $_" }

try {
    Invoke-Api 'Patch' "/stations/$($stations['galle'])/deactivate" $null $backoffice | Out-Null
    Write-Output 'Station deactivated: Galle Fort Energy Point'
}
catch { Write-Output "Station deactivate skipped: $_" }

$maintenanceSlot = Get-Slot 'malabe' 4 3
if ($maintenanceSlot) {
    try {
        Invoke-Api 'Patch' "/slots/$($maintenanceSlot.id)/availability" @{ status = 'Unavailable' } $operatorSessions['malabe'] | Out-Null
        Write-Output 'Slot marked Unavailable by Malabe operator (maintenance window)'
    }
    catch { Write-Output "Slot availability skipped: $_" }
}

Write-Output 'Demo data ready.'
