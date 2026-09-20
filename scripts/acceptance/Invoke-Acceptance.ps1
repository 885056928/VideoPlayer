#Requires -Version 7
$ErrorActionPreference = 'Stop'

function Assert-PathUnder {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Parent
    )

    $canonicalPath = [IO.Path]::GetFullPath($Path)
    $canonicalParent = [IO.Path]::GetFullPath($Parent).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if (Test-Path -LiteralPath $canonicalPath) {
        $canonicalPath = [IO.Path]::GetFullPath(
            (Resolve-Path -LiteralPath $canonicalPath -ErrorAction Stop).Path)
    }

    $relative = [IO.Path]::GetRelativePath($canonicalParent, $canonicalPath)
    if ([IO.Path]::IsPathRooted($relative) -or
        $relative -eq '..' -or
        $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal)) {
        throw "Refusing path outside $canonicalParent`: $canonicalPath"
    }

    return $canonicalPath
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$output = Assert-PathUnder -Path (Join-Path $artifactsRoot 'acceptance') -Parent $artifactsRoot
New-Item -ItemType Directory -Force -Path $output | Out-Null

$commands = Join-Path $output 'commands.txt'
$build = Join-Path $output 'build-result.txt'
$testConsole = Join-Path $output 'test-console.txt'
$tests = Join-Path $output 'test-results.trx'
$coverage = Join-Path $output 'coverage.cobertura.xml'
$solution = Join-Path $root 'VideoPlayer.sln'

$commandLog = @(
    'Building Release...',
    "dotnet restore `"$solution`"",
    "dotnet build `"$solution`" -c Release --no-restore",
    'Running tests with Coverlet...',
    "dotnet test `"$solution`" -c Release --no-build -p:CollectCoverage=true -p:CoverletOutputFormat=cobertura -p:CoverletOutput=`"$coverage`" --logger `"trx;LogFileName=$tests`"",
    "& scripts/acceptance/Test-Coverage.ps1 -CoveragePath `"$coverage`"",
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

if (Test-Path -LiteralPath $coverage) {
    Remove-Item -LiteralPath $coverage -Force
}

$testArgs = @(
    'test',
    $solution,
    '-c', 'Release',
    '--no-build',
    '-p:CollectCoverage=true',
    '-p:CoverletOutputFormat=cobertura',
    "-p:CoverletOutput=$coverage",
    '--logger', "trx;LogFileName=$tests"
)
$testOutput = & dotnet @testArgs 2>&1
$testExitCode = $LASTEXITCODE
$testOutput | Tee-Object -FilePath $testConsole
if ($testExitCode -ne 0) {
    exit $testExitCode
}
if (-not (Test-Path -LiteralPath $coverage -PathType Leaf)) {
    throw "Coverlet did not generate the expected coverage report: $coverage"
}

$coverageScript = Join-Path $PSScriptRoot 'Test-Coverage.ps1'
$coverageOutput = & $coverageScript -CoveragePath $coverage 2>&1
$coverageExitCode = $LASTEXITCODE
$coverageOutput | ForEach-Object { Write-Host $_ }
if ($coverageOutput.Count -gt 0) {
    Add-Content -LiteralPath $commands -Value $coverageOutput
}
if ($coverageExitCode -ne 0) {
    exit $coverageExitCode
}

$architectureScript = Join-Path $PSScriptRoot 'Test-Architecture.ps1'
$architectureOutput = & $architectureScript 2>&1
$architectureExitCode = $LASTEXITCODE
$architectureOutput | ForEach-Object { Write-Host $_ }
if ($architectureOutput.Count -gt 0) {
    Add-Content -LiteralPath $commands -Value $architectureOutput
}
if ($architectureExitCode -ne 0) {
    exit $architectureExitCode
}

Write-Host "Acceptance completed. Evidence: $output"