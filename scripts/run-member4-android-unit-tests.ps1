param(
    [string]$JunitJar,
    [string]$HamcrestJar
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$androidRoot = Join-Path $repositoryRoot 'SolarGridAndroid'
$gradleHome = if ($env:GRADLE_USER_HOME) {
    $env:GRADLE_USER_HOME
}
else {
    Join-Path $env:USERPROFILE '.gradle'
}

if (-not $JunitJar) {
    $JunitJar = Get-ChildItem `
        (Join-Path $gradleHome 'caches\modules-2\files-2.1\junit\junit\4.13.2') `
        -Recurse `
        -Filter 'junit-4.13.2.jar' `
        -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $HamcrestJar) {
    $HamcrestJar = Get-ChildItem `
        (Join-Path $gradleHome 'caches\modules-2\files-2.1\org.hamcrest\hamcrest-core\1.3') `
        -Recurse `
        -Filter 'hamcrest-core-1.3.jar' `
        -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $JunitJar -or -not (Test-Path -LiteralPath $JunitJar) -or
    -not $HamcrestJar -or -not (Test-Path -LiteralPath $HamcrestJar)) {
    throw 'JUnit 4.13.2 and Hamcrest 1.3 must exist in the Gradle cache or be supplied by path.'
}

$outputBase = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\android-junit'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $outputBase "run-$PID"))
if (-not $outputRoot.StartsWith($outputBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The Android test output path is outside the ignored artifact directory.'
}
$classesDirectory = Join-Path $outputRoot 'classes'
New-Item -ItemType Directory -Path $classesDirectory -Force | Out-Null
$localJunitJar = Join-Path $outputRoot 'junit-4.13.2.jar'
$localHamcrestJar = Join-Path $outputRoot 'hamcrest-core-1.3.jar'
Copy-Item -LiteralPath $JunitJar -Destination $localJunitJar
Copy-Item -LiteralPath $HamcrestJar -Destination $localHamcrestJar

$mainSources = @(
    'app/src/main/java/com/solargrid/exchange/data/model/DashboardReservation.java',
    'app/src/main/java/com/solargrid/exchange/data/model/DashboardStatusSummary.java',
    'app/src/main/java/com/solargrid/exchange/data/model/ProsumerDashboard.java',
    'app/src/main/java/com/solargrid/exchange/data/model/Reservation.java',
    'app/src/main/java/com/solargrid/exchange/data/model/ReservationAllowedActions.java',
    'app/src/main/java/com/solargrid/exchange/data/model/ReservationStatusHistory.java',
    'app/src/main/java/com/solargrid/exchange/data/model/SessionUser.java',
    'app/src/main/java/com/solargrid/exchange/data/model/SharedReservationContract.java',
    'app/src/main/java/com/solargrid/exchange/features/dashboard/BookingHistoryQuery.java',
    'app/src/main/java/com/solargrid/exchange/features/operations/CameraPermissionState.java',
    'app/src/main/java/com/solargrid/exchange/features/operations/OperatorFlowPolicy.java',
    'app/src/main/java/com/solargrid/exchange/features/operations/RoleRoutePolicy.java',
    'app/src/main/java/com/solargrid/exchange/features/operations/TransactionErrorMapper.java',
    'app/src/main/java/com/solargrid/exchange/features/transactions/QrExpiry.java',
    'app/src/main/java/com/solargrid/exchange/features/transactions/QrTokenPolicy.java',
    'app/src/main/java/com/solargrid/exchange/network/ApiError.java',
    'app/src/main/java/com/solargrid/exchange/ui/dashboard/ProsumerPresentation.java'
)
$testSources = @(
    'app/src/test/java/com/solargrid/exchange/data/model/SharedReservationContractTest.java',
    'app/src/test/java/com/solargrid/exchange/features/dashboard/BookingHistoryQueryTest.java',
    'app/src/test/java/com/solargrid/exchange/features/operations/CameraPermissionStateTest.java',
    'app/src/test/java/com/solargrid/exchange/features/operations/OperatorFlowPolicyTest.java',
    'app/src/test/java/com/solargrid/exchange/features/operations/RoleRoutePolicyTest.java',
    'app/src/test/java/com/solargrid/exchange/features/operations/TransactionErrorMapperTest.java',
    'app/src/test/java/com/solargrid/exchange/features/transactions/QrExpiryTest.java',
    'app/src/test/java/com/solargrid/exchange/features/transactions/QrTokenPolicyTest.java',
    'app/src/test/java/com/solargrid/exchange/ui/dashboard/ProsumerPresentationTest.java'
)
$testClasses = @(
    'com.solargrid.exchange.data.model.SharedReservationContractTest',
    'com.solargrid.exchange.features.dashboard.BookingHistoryQueryTest',
    'com.solargrid.exchange.features.operations.CameraPermissionStateTest',
    'com.solargrid.exchange.features.operations.OperatorFlowPolicyTest',
    'com.solargrid.exchange.features.operations.RoleRoutePolicyTest',
    'com.solargrid.exchange.features.operations.TransactionErrorMapperTest',
    'com.solargrid.exchange.features.transactions.QrExpiryTest',
    'com.solargrid.exchange.features.transactions.QrTokenPolicyTest',
    'com.solargrid.exchange.ui.dashboard.ProsumerPresentationTest'
)

try {
    $classpath = "$localJunitJar$([IO.Path]::PathSeparator)$localHamcrestJar"
    $sources = ($mainSources + $testSources) |
        ForEach-Object { Join-Path $androidRoot $_ }
    & javac -encoding UTF-8 --release 17 `
        -cp $classpath -d $classesDirectory $sources
    if ($LASTEXITCODE -ne 0) {
        throw "javac failed with exit code $LASTEXITCODE."
    }

    $runtimeClasspath =
        "$classesDirectory$([IO.Path]::PathSeparator)$classpath"
    & java -cp $runtimeClasspath org.junit.runner.JUnitCore $testClasses
    if ($LASTEXITCODE -ne 0) {
        throw "JUnit failed with exit code $LASTEXITCODE."
    }
}
finally {
    $resolvedOutput = [IO.Path]::GetFullPath($outputRoot)
    if ((Test-Path -LiteralPath $resolvedOutput) -and
        $resolvedOutput.StartsWith($outputBase, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedOutput).StartsWith('run-')) {
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }
}
