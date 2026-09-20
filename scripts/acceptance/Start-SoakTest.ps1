[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$MediaFolder,
    [ValidateRange(0.0001, 24.0)] [double]$DurationHours = 8,
    [ValidateRange(1, 3600)] [int]$IntervalSeconds = 60
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

if ($null -eq ('VideoPlayer.Acceptance.NativeMethods' -as [type])) {
    Add-Type -TypeDefinition (@(
        'using System;',
        'using System.Runtime.InteropServices;',
        '',
        'namespace VideoPlayer.Acceptance',
        '{',
        '    public static class NativeMethods',
        '    {',
        '        [DllImport("user32.dll", SetLastError = true)]',
        '        public static extern uint GetGuiResources(IntPtr processHandle, uint flags);',
        '    }',
        '}'
    ) -join [Environment]::NewLine)
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$output = Assert-PathUnder -Path (Join-Path $artifactsRoot 'acceptance') -Parent $artifactsRoot
$executable = Join-Path $root 'artifacts/publish/win-x64/VideoPlayer.exe'
$csvPath = Join-Path $output 'soak.csv'

$mediaPath = (Resolve-Path -LiteralPath $MediaFolder).Path
if (-not (Test-Path -LiteralPath $mediaPath -PathType Container)) {
    throw "Media folder is not a directory: $mediaPath"
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published executable not found. Run Publish-Release.ps1 first: $executable"
}

$supportedExtensions = @(
    '.mp4', '.mkv', '.avi', '.mov', '.wmv', '.ts', '.m2ts', '.webm'
)
$firstMedia = Get-ChildItem -LiteralPath $mediaPath -File -Recurse |
    Where-Object { $supportedExtensions -contains $_.Extension.ToLowerInvariant() } |
    Select-Object -First 1
if ($null -eq $firstMedia) {
    throw "No supported media file was found under: $mediaPath"
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

function Get-SoakSample {
    param([Parameter(Mandatory)] [Diagnostics.Process]$Process)

    try {
        $Process.Refresh()
        if ($Process.HasExited) {
            return $null
        }

        $processHandle = $Process.Handle
        $gdiObjects = if ($processHandle -ne [IntPtr]::Zero) {
            [int][VideoPlayer.Acceptance.NativeMethods]::GetGuiResources($processHandle, 0)
        } else {
            $null
        }
        $userObjects = if ($processHandle -ne [IntPtr]::Zero) {
            [int][VideoPlayer.Acceptance.NativeMethods]::GetGuiResources($processHandle, 1)
        } else {
            $null
        }

        return [PSCustomObject]@{
            TimestampUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
            ElapsedSeconds = [Math]::Round($script:stopwatch.Elapsed.TotalSeconds, 3)
            WorkingSetMB = [Math]::Round($Process.WorkingSet64 / 1MB, 2)
            PrivateMemoryMB = [Math]::Round($Process.PrivateMemorySize64 / 1MB, 2)
            HandleCount = $Process.HandleCount
            ThreadCount = $Process.Threads.Count
            GdiObjects = $gdiObjects
            UserObjects = $userObjects
            MediaFolder = $script:mediaPath
            ProcessId = $Process.Id
        }
    } catch [InvalidOperationException] {
        return $null
    }
}

$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$records = [Collections.Generic.List[object]]::new()
$process = $null

try {
    Write-Host "Starting published VideoPlayer: $executable"
    Write-Host "Media folder: $mediaPath"
    Write-Host "Open this folder in VideoPlayer before beginning timed operations: $firstMedia"
    Write-Host "Sampling every $IntervalSeconds second(s) for up to $DurationHours hour(s)."

    $process = Start-Process `
        -FilePath $executable `
        -WorkingDirectory (Split-Path -Parent $executable) `
        -PassThru
    if ($null -eq $process) {
        throw 'Start-Process did not return a process object.'
    }

    while (-not $process.HasExited) {
        $sample = Get-SoakSample -Process $process
        if ($null -eq $sample) {
            break
        }

        $records.Add($sample)
        $records | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8

        $remaining = [TimeSpan]::FromHours($DurationHours) - $stopwatch.Elapsed
        if ($remaining -le [TimeSpan]::Zero) {
            break
        }

        $sleepSeconds = [Math]::Min([double]$IntervalSeconds, $remaining.TotalSeconds)
        if ($sleepSeconds -le 0) {
            break
        }

        Start-Sleep -Milliseconds ([int][Math]::Ceiling($sleepSeconds * 1000))
    }

    if ($process.HasExited) {
        Write-Host "VideoPlayer exited with code $($process.ExitCode)."
    } else {
        Write-Host 'Soak duration reached; closing the monitored process.'
    }
} finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $closed = $process.CloseMainWindow()
        if ($closed) {
            $null = $process.WaitForExit(10000)
        }
        if (-not $process.HasExited) {
            Stop-Process -InputObject $process -Force
        }
    }

    if ($null -ne $process) {
        $process.Dispose()
    }
}

Write-Host "Soak samples written to $csvPath"