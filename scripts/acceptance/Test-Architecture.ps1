$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sourceRoots = @(
    (Join-Path $root 'VideoPlayer/Views'),
    (Join-Path $root 'VideoPlayer/ViewModels')
)

$files = @(
    Get-ChildItem -LiteralPath $sourceRoots -Filter '*.cs' -File -Recurse
)
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
    $violations | ForEach-Object { Write-Error -Message $_ }
    exit 1
}

Write-Host "Architecture gate passed. Checked $($files.Count) files."