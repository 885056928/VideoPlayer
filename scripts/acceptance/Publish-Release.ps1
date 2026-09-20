[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64'
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

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$output = Assert-PathUnder -Path (Join-Path $artifactsRoot 'publish/win-x64') -Parent $artifactsRoot
$project = Join-Path $root 'VideoPlayer/VideoPlayer.csproj'
$sourceLicenses = Join-Path $root 'LICENSES'

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Project file not found: $project"
}
if (-not (Test-Path -LiteralPath $sourceLicenses -PathType Container)) {
    throw "License directory not found: $sourceLicenses"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $output | Out-Null

$publishArgs = @(
    'publish',
    $project,
    '-c', $Configuration,
    '-r', $RuntimeIdentifier,
    '--self-contained', 'true',
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-p:ReadyToRun=false',
    '-p:PublishReadyToRun=false',
    '-o', $output
)
$publishOutput = & dotnet @publishArgs 2>&1
$publishExitCode = $LASTEXITCODE
$publishOutput | ForEach-Object { Write-Host $_ }
if ($publishExitCode -ne 0) {
    exit $publishExitCode
}

Copy-Item -LiteralPath $sourceLicenses -Destination $output -Recurse -Force
$destinationLicenses = Join-Path $output 'LICENSES'
if (-not (Test-Path -LiteralPath $destinationLicenses -PathType Container)) {
    throw "Published license directory was not created: $destinationLicenses"
}

$assetsFile = Join-Path $root 'VideoPlayer/obj/project.assets.json'
if (-not (Test-Path -LiteralPath $assetsFile -PathType Leaf)) {
    throw "Project assets file not found after publish: $assetsFile"
}

$nugetRoot = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    Join-Path $env:USERPROFILE '.nuget/packages'
} else {
    $env:NUGET_PACKAGES
}
$nugetRoot = [IO.Path]::GetFullPath($nugetRoot)
$assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json
$packageNotices = [Collections.Generic.List[string]]::new()
$packageLicensesRoot = Join-Path $destinationLicenses 'packages'

foreach ($libraryProperty in $assets.libraries.PSObject.Properties) {
    if ($libraryProperty.Value.type -ne 'package') {
        continue
    }

    if ($libraryProperty.Name -notmatch '^(?<id>.+)/(?<version>[^/]+)$') {
        continue
    }

    $packageId = $Matches.id
    $packageVersion = $Matches.version
    $packageDirectory = Join-Path $nugetRoot (Join-Path $packageId.ToLowerInvariant() $packageVersion)
    if (-not (Test-Path -LiteralPath $packageDirectory -PathType Container)) {
        throw "NuGet package directory not found: $packageDirectory"
    }

    $nuspec = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' -File |
        Select-Object -First 1
    if ($null -ne $nuspec) {
        [xml]$nuspecXml = Get-Content -LiteralPath $nuspec.FullName -Raw
        $metadata = $nuspecXml.package.metadata
        $license = if ($metadata.license.'#text') {
            $metadata.license.'#text'
        } elseif ($metadata.licenseUrl) {
            $metadata.licenseUrl
        } else {
            'See package files'
        }
        $packageNotices.Add(
            "$packageId`t$packageVersion`t$license`t$($metadata.authors)`t$($metadata.projectUrl)")
    } else {
        $packageNotices.Add("$packageId`t$packageVersion`tSee package files`t`t")
    }

    $licenseFiles = Get-ChildItem -LiteralPath $packageDirectory -Recurse -File |
        Where-Object {
            $_.Name -match '(?i)(license|licence|notice|copying|copyright)'
        }
    foreach ($licenseFile in $licenseFiles) {
        $relativeLicensePath = [IO.Path]::GetRelativePath($packageDirectory, $licenseFile.FullName)
        $destination = Assert-PathUnder `
            -Path (Join-Path $packageLicensesRoot (Join-Path $packageId (Join-Path $packageVersion $relativeLicensePath))) `
            -Parent $destinationLicenses
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        Copy-Item -LiteralPath $licenseFile.FullName -Destination $destination -Force
    }
}

$packageNotices.Sort([StringComparer]::OrdinalIgnoreCase)
$packageNoticeFile = Join-Path $destinationLicenses 'PACKAGE-NOTICES.txt'
$noticeLines = @(
    'VideoPlayer publish package dependency notices',
    'Generated from VideoPlayer/obj/project.assets.json at publish time.',
    'Fields: PackageId<TAB>Version<TAB>License<TAB>Authors<TAB>ProjectUrl',
    ''
) + $packageNotices
[IO.File]::WriteAllLines(
    $packageNoticeFile,
    $noticeLines,
    [Text.UTF8Encoding]::new($false))

$requiredPublishedFiles = @(
    'VideoPlayer.exe',
    'VideoPlayer.deps.json',
    'VideoPlayer.runtimeconfig.json',
    'libvlc/win-x64/libvlc.dll',
    'libvlc/win-x64/libvlccore.dll'
)
foreach ($requiredFile in $requiredPublishedFiles) {
    $requiredPath = Join-Path $output $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Published file is missing: $requiredPath"
    }
}
$pluginsDirectory = Join-Path $output 'libvlc/win-x64/plugins'
if (-not (Test-Path -LiteralPath $pluginsDirectory -PathType Container)) {
    throw "LibVLC plugins directory is missing: $pluginsDirectory"
}

Write-Host "Published to $output"