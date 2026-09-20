[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$CoveragePath
)

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

function Get-CoverageRows {
    param([Parameter(Mandatory)] [string]$Path)

    [xml]$coverage = Get-Content -LiteralPath $Path -Raw
    $classElements = @($coverage.coverage.packages.package.classes.class)
    $rawRows = [Collections.Generic.List[object]]::new()

    foreach ($classElement in $classElements) {
        $file = ([string]$classElement.filename).Replace('\', '/')
        foreach ($lineElement in @($classElement.lines.line)) {
            $branchCovered = 0
            $branchTotal = 0
            $conditionCoverage = [string]$lineElement.'condition-coverage'
            if ($conditionCoverage -match '^(\d+)%\s*\((\d+)/(\d+)\)$') {
                $branchCovered = [int]$Matches[2]
                $branchTotal = [int]$Matches[3]
            }

            $rawRows.Add([PSCustomObject]@{
                File = $file
                Line = [int]$lineElement.number
                Hits = [int]$lineElement.hits
                BranchCovered = $branchCovered
                BranchTotal = $branchTotal
            })
        }
    }

    return @(
        $rawRows |
            Group-Object File, Line |
            ForEach-Object {
                [PSCustomObject]@{
                    File = $_.Group[0].File
                    Line = $_.Group[0].Line
                    Hits = ($_.Group | Measure-Object Hits -Maximum).Maximum
                    BranchCovered = ($_.Group | Measure-Object BranchCovered -Maximum).Maximum
                    BranchTotal = ($_.Group | Measure-Object BranchTotal -Maximum).Maximum
                }
            }
    )
}

function Test-CoverageScope {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$File
    )

    $normalizedFile = $File.Replace('\', '/')
    if ($normalizedFile.StartsWith('VideoPlayer/', [StringComparison]::OrdinalIgnoreCase)) {
        $normalizedFile = $normalizedFile.Substring('VideoPlayer/'.Length)
    }
    else {
        $marker = '/VideoPlayer/'
        $markerIndex = $normalizedFile.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase)
        if ($markerIndex -ge 0) {
            $normalizedFile = $normalizedFile.Substring($markerIndex + $marker.Length)
        }
    }

    switch ($Name) {
        'Services' { return $normalizedFile.StartsWith('Services/', [StringComparison]::OrdinalIgnoreCase) }
        'Models' { return $normalizedFile.StartsWith('Models/', [StringComparison]::OrdinalIgnoreCase) }
        'Helpers' { return $normalizedFile.StartsWith('Helpers/', [StringComparison]::OrdinalIgnoreCase) }
        'PlaybackStateMachine' {
            return $normalizedFile.Equals(
                'Services/PlaybackStateMachine.cs',
                [StringComparison]::OrdinalIgnoreCase)
        }
        'Required' {
            return $normalizedFile.StartsWith('Services/', [StringComparison]::OrdinalIgnoreCase) -or
                $normalizedFile.StartsWith('Models/', [StringComparison]::OrdinalIgnoreCase) -or
                $normalizedFile.StartsWith('Helpers/', [StringComparison]::OrdinalIgnoreCase)
        }
        default { throw "Unknown coverage scope: $Name" }
    }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$coveragePath = Assert-PathUnder -Path $CoveragePath -Parent $artifactsRoot
if (-not (Test-Path -LiteralPath $coveragePath -PathType Leaf)) {
    throw "Coverage report not found: $coveragePath"
}

$rows = @(Get-CoverageRows -Path $coveragePath)
$scopes = @(
    [PSCustomObject]@{ Name = 'Services'; LineThreshold = 80.0; BranchThreshold = 70.0 }
    [PSCustomObject]@{ Name = 'Models'; LineThreshold = 80.0; BranchThreshold = 70.0 }
    [PSCustomObject]@{ Name = 'Helpers'; LineThreshold = 80.0; BranchThreshold = 70.0 }
    [PSCustomObject]@{ Name = 'PlaybackStateMachine'; LineThreshold = 80.0; BranchThreshold = 70.0 }
    [PSCustomObject]@{ Name = 'Required'; LineThreshold = 80.0; BranchThreshold = 70.0 }
)

$results = [Collections.Generic.List[object]]::new()
foreach ($scope in $scopes) {
    $scopeRows = @(
        $rows | Where-Object {
            Test-CoverageScope -Name $scope.Name -File $_.File
        }
    )
    if ($scopeRows.Count -eq 0) {
        throw "No coverage rows were found for scope $($scope.Name)."
    }

    $lineCovered = @($scopeRows | Where-Object Hits -gt 0).Count
    $branchCovered = [int](($scopeRows | Measure-Object BranchCovered -Sum).Sum ?? 0)
    $branchTotal = [int](($scopeRows | Measure-Object BranchTotal -Sum).Sum ?? 0)
    $linePercent = [Math]::Round(100.0 * $lineCovered / $scopeRows.Count, 2)
    $branchPercent = if ($branchTotal -eq 0) {
        100.0
    } else {
        [Math]::Round(100.0 * $branchCovered / $branchTotal, 2)
    }

    $passed = $linePercent -ge $scope.LineThreshold -and
        $branchPercent -ge $scope.BranchThreshold
    $results.Add([PSCustomObject]@{
        Scope = $scope.Name
        LineCovered = $lineCovered
        LineTotal = $scopeRows.Count
        LinePercent = $linePercent
        BranchCovered = $branchCovered
        BranchTotal = $branchTotal
        BranchPercent = $branchPercent
        LineThreshold = $scope.LineThreshold
        BranchThreshold = $scope.BranchThreshold
        Passed = $passed
    })
}

$summaryLines = [Collections.Generic.List[string]]::new()
$summaryLines.Add(
    'Scope,LineCovered,LineTotal,LinePercent,BranchCovered,BranchTotal,BranchPercent,LineThreshold,BranchThreshold,Passed')
foreach ($result in $results) {
    $summaryLines.Add(
        ('{0},{1},{2},{3:0.00},{4},{5},{6:0.00},{7:0.00},{8:0.00},{9}' -f
            $result.Scope,
            $result.LineCovered,
            $result.LineTotal,
            $result.LinePercent,
            $result.BranchCovered,
            $result.BranchTotal,
            $result.BranchPercent,
            $result.LineThreshold,
            $result.BranchThreshold,
            $result.Passed))
}

$summaryPath = Join-Path (Split-Path -Parent $coveragePath) 'coverage-summary.txt'
[IO.File]::WriteAllLines($summaryPath, $summaryLines, [Text.UTF8Encoding]::new($false))
$summaryLines | Write-Output

$failures = @($results | Where-Object { -not $_.Passed })
if ($failures.Count -gt 0) {
    $failureText = ($failures | ForEach-Object {
        "$($_.Scope): line $($_.LinePercent)% >= $($_.LineThreshold)%, branch $($_.BranchPercent)% >= $($_.BranchThreshold)%"
    }) -join '; '
    [Console]::Error.WriteLine("Coverage gate failed: $failureText")
    exit 1
}

Write-Output "Coverage gate passed. Summary: $summaryPath"
