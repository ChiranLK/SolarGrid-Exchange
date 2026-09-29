param(
    [int]$MongoPort = 27019,
    [int]$ApiPort = 5088
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "solargrid-deployment-check-$PID"
$apiProcess = $null

try {
    docker run --detach --rm --name $containerName `
        --publish "127.0.0.1:${MongoPort}:${MongoPort}" `
        mongo:8.0 `
        mongod --replSet rs0 --bind_ip_all --port $MongoPort | Out-Null

    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $ping = docker exec $containerName mongosh --quiet --port $MongoPort `
                --eval "db.adminCommand({ ping: 1 }).ok" 2>$null
            if (($ping | Select-Object -Last 1) -eq '1') {
                $ready = $true
                break
            }
        }
        catch {
            # MongoDB is still starting inside the bounded verification window.
        }

        Start-Sleep -Seconds 1
    }

    if (-not $ready) {
        throw 'MongoDB verification container did not become ready.'
    }

    docker exec $containerName mongosh --quiet --port $MongoPort `
        --eval "rs.initiate({_id:'rs0',members:[{_id:0,host:'localhost:$MongoPort'}]}).ok" | Out-Null

    $primary = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $state = docker exec $containerName mongosh --quiet --port $MongoPort `
            --eval 'db.hello().isWritablePrimary'
        if (($state | Select-Object -Last 1) -eq 'true') {
            $primary = $true
            break
        }

        Start-Sleep -Seconds 1
    }

    if (-not $primary) {
        throw 'MongoDB verification replica set did not elect a primary.'
    }

    $secretBytes = New-Object byte[] 48
    $randomNumberGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $randomNumberGenerator.GetBytes($secretBytes)
    }
    finally {
        $randomNumberGenerator.Dispose()
    }

    $apiEnvironment = @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        MongoSettings__ConnectionString = "mongodb://localhost:$MongoPort/?replicaSet=rs0&directConnection=true"
        JwtSettings__Key = [Convert]::ToBase64String($secretBytes)
        OpenApi__Enabled = 'true'
    }
    foreach ($entry in $apiEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }

    $apiProcess = Start-Process dotnet `
        -ArgumentList @(
            'run',
            '--project', 'SolarMicrogrid.API/SolarMicrogrid.API.csproj',
            '--configuration', 'Release',
            '--no-build',
            '--no-launch-profile',
            '--urls', "http://127.0.0.1:$ApiPort") `
        -WorkingDirectory $repositoryRoot `
        -WindowStyle Hidden `
        -PassThru

    $healthUri = "http://127.0.0.1:$ApiPort/health"
    $healthy = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $healthResponse = Invoke-WebRequest -Uri $healthUri -UseBasicParsing
            if ($healthResponse.StatusCode -eq 200) {
                $healthy = $true
                break
            }
        }
        catch {
            # API/index startup is still in progress inside the bounded verification window.
        }

        Start-Sleep -Seconds 1
    }

    if (-not $healthy) {
        throw 'API health endpoint did not become healthy.'
    }

    $health = $healthResponse.Content | ConvertFrom-Json
    if ($health.status -ne 'Healthy' -or
        $health.checks.api -ne 'Healthy' -or
        $health.checks.database -ne 'Healthy' -or
        [string]::IsNullOrWhiteSpace($health.correlationId)) {
        throw 'Health response did not match the safe deployment contract.'
    }

    $document = Invoke-RestMethod -Uri "http://127.0.0.1:$ApiPort/openapi/v1.json"
    if ($document.components.securitySchemes.Bearer.scheme -ne 'bearer') {
        throw 'OpenAPI bearer scheme is missing.'
    }

    $operations = @(
        $document.paths.'/api/dashboard'.get,
        $document.paths.'/api/dashboard/history'.get,
        $document.paths.'/api/transactions/reservations/{reservationId}/qr'.post,
        $document.paths.'/api/transactions/verify'.post,
        $document.paths.'/api/transactions/reservations/{reservationId}/complete'.post
    )
    foreach ($operation in $operations) {
        if ($null -eq $operation -or
            $operation.security.Count -eq 0 -or
            $operation.responses.PSObject.Properties.Name -notcontains '200' -or
            [string]::IsNullOrWhiteSpace($operation.summary) -or
            [string]::IsNullOrWhiteSpace($operation.description)) {
            throw 'A Member 4 OpenAPI operation is missing required metadata.'
        }
    }

    $verifyOperation = $document.paths.'/api/transactions/verify'.post
    $completeOperation = $document.paths.'/api/transactions/reservations/{reservationId}/complete'.post
    if ($null -eq $verifyOperation.requestBody.content.'application/json'.example -or
        $null -eq $verifyOperation.responses.'200'.content.'application/json'.example -or
        $null -eq $completeOperation.requestBody.content.'application/json'.example -or
        $null -eq $completeOperation.responses.'200'.content.'application/json'.example) {
        throw 'Member 4 OpenAPI safe examples are missing.'
    }

    $corsResponse = Invoke-WebRequest `
        -Uri "http://127.0.0.1:$ApiPort/api/dashboard" `
        -Method Options `
        -Headers @{
            Origin = 'http://localhost:5173'
            'Access-Control-Request-Method' = 'GET'
            'Access-Control-Request-Headers' = 'authorization'
        } `
        -UseBasicParsing
    if ($corsResponse.Headers['Access-Control-Allow-Origin'] -ne 'http://localhost:5173') {
        throw 'Configured development CORS origin was not allowed.'
    }

    $unlistedCorsResponse = Invoke-WebRequest `
        -Uri "http://127.0.0.1:$ApiPort/api/dashboard" `
        -Method Options `
        -Headers @{
            Origin = 'https://unlisted.example.invalid'
            'Access-Control-Request-Method' = 'GET'
            'Access-Control-Request-Headers' = 'authorization'
        } `
        -UseBasicParsing
    if ($unlistedCorsResponse.Headers['Access-Control-Allow-Origin']) {
        throw 'An unlisted CORS origin was unexpectedly allowed.'
    }

    Write-Output 'Health endpoint: Passed'
    Write-Output 'OpenAPI bearer/routes/status metadata/examples: Passed'
    Write-Output 'Configured/unlisted CORS preflight: Passed'
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
        $apiProcess.WaitForExit()
    }

    docker rm --force $containerName 2>$null | Out-Null
}
