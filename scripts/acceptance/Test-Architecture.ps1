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
$sourceRoots = @(
    (Assert-PathUnder -Path (Join-Path $root 'VideoPlayer/Views') -Parent $root),
    (Assert-PathUnder -Path (Join-Path $root 'VideoPlayer/ViewModels') -Parent $root)
)
foreach ($sourceRoot in $sourceRoots) {
    if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
        [Console]::Error.WriteLine("Architecture source directory not found: $sourceRoot")
        exit 1
    }
}

$files = @(
    Get-ChildItem -LiteralPath $sourceRoots -Filter '*.cs' -File -Recurse
)
if ($files.Count -eq 0) {
    [Console]::Error.WriteLine('Architecture gate found no source files to inspect.')
    exit 1
}

$forbidden = @(
    'MediaPlayer.Play(',
    'MediaPlayer.Pause(',
    'MediaPlayer.Stop(',
    'MediaPlayer.Time',
    'MediaPlayer.SetAudioTrack(',
    'MediaPlayer.SetSpu('
)
$violations = @()
foreach ($file in $files) {
    $content = [IO.File]::ReadAllText($file.FullName)
    foreach ($token in $forbidden) {
        if ($content.Contains($token, [StringComparison]::Ordinal)) {
            $violations += "$($file.FullName): $token"
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object {
        Write-Output "Architecture violation: $_"
    }
    [Console]::Error.WriteLine(
        "Architecture gate failed with $($violations.Count) violation(s).")
    exit 1
}

Write-Output "Architecture gate passed. Checked $($files.Count) files."