$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$output = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'acceptance'))
$relativeOutput = [IO.Path]::GetRelativePath($artifactsRoot, $output)
if ([IO.Path]::IsPathRooted($relativeOutput) -or
    $relativeOutput -eq '..' -or
    $relativeOutput.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal)) {
    throw "Refusing to write acceptance evidence outside artifacts: $output"
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
$commands = Join-Path $output 'commands.txt'
$build = Join-Path $output 'build-result.txt'
$testConsole = Join-Path $output 'test-console.txt'
$tests = Join-Path $output 'test-results.trx'
$solution = Join-Path $root 'VideoPlayer.sln'

$commandLog = @(
    'Building Release...',
    "dotnet restore `"$solution`"",
    "dotnet build `"$solution`" -c Release --no-restore",
    'Running tests...',
    "dotnet test `"$solution`" -c Release --no-build --logger `"trx;LogFileName=$tests`"",
    '& scripts/acceptance/Test-Architecture.ps1'
)
$commandLog | Set-Content -LiteralPath $commands -Encoding utf8

$restoreArgs = @('restore', $solution)
$restoreOutput = & dotnet @restoreArgs 2>&1
$restoreExitCode = $LASTEXITCODE
$restoreOutput | Tee-Object -FilePath $commands -Append
if ($restoreExitCode -ne 0) {
    exit $restoreExitCode
}

$buildArgs = @('build', $solution, '-c', 'Release', '--no-restore')
$buildOutput = & dotnet @buildArgs 2>&1
$buildExitCode = $LASTEXITCODE
$buildOutput | Tee-Object -FilePath $build
if ($buildExitCode -ne 0) {
    exit $buildExitCode
}

$testArgs = @(
    'test',
    $solution,
    '-c', 'Release',
    '--no-build',
    '--logger', "trx;LogFileName=$tests"
)
$testOutput = & dotnet @testArgs 2>&1
$testExitCode = $LASTEXITCODE
$testOutput | Tee-Object -FilePath $testConsole
if ($testExitCode -ne 0) {
    exit $testExitCode
}

$architectureScript = Join-Path $PSScriptRoot 'Test-Architecture.ps1'
$architectureOutput = & $architectureScript 2>&1
$architectureExitCode = $LASTEXITCODE
$architectureOutput | Tee-Object -FilePath $commands -Append
if ($architectureExitCode -ne 0) {
    exit $architectureExitCode
}

Write-Host "Acceptance completed. Evidence: $output"