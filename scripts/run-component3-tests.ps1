param(
    [int]$MongoPort = 27018
)

$ErrorActionPreference = 'Stop'
$containerName = "solargrid-component3-tests-$PID"
$mongoUri = "mongodb://localhost:$MongoPort/?replicaSet=rs0&directConnection=true"
$testExitCode = 1

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
            # MongoDB is still starting; retry within the bounded loop.
        }

        Start-Sleep -Seconds 1
    }

    if (-not $ready) {
        throw 'MongoDB test container did not become ready.'
    }

    docker exec $containerName mongosh --quiet --port $MongoPort `
        --eval "rs.initiate({_id:'rs0',members:[{_id:0,host:'localhost:$MongoPort'}]}).ok" | Out-Null

    $primary = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $state = docker exec $containerName mongosh --quiet --port $MongoPort `
            --eval "db.hello().isWritablePrimary"
        if (($state | Select-Object -Last 1) -eq 'true') {
            $primary = $true
            break
        }

        Start-Sleep -Seconds 1
    }

    if (-not $primary) {
        throw 'MongoDB test replica set did not elect a primary.'
    }

    $env:COMPONENT3_TEST_MONGODB_URI = $mongoUri
    & dotnet test SolarMicrogrid.API.Tests/SolarMicrogrid.API.Tests.csproj `
        --configuration Release `
        --no-build `
        --no-restore `
        --logger "console;verbosity=minimal"
    $testExitCode = $LASTEXITCODE
}
finally {
    docker rm --force $containerName 2>$null | Out-Null
}

exit $testExitCode
