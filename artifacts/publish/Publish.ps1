#Requires -Version 7
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$publisher = Join-Path $root 'scripts/acceptance/Publish-Release.ps1'
if (-not (Test-Path -LiteralPath $publisher -PathType Leaf)) {
    throw "找不到项目发布脚本：$publisher"
}

# 使用独立进程获取真实退出码，发布失败时禁止将旧目录打包为新版本。
& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $publisher
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$output = Join-Path $PSScriptRoot 'win-x64'
$executable = Join-Path $output 'VideoPlayer.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "发布未生成运行程序：$executable"
}

# 每次生成独立的发布包，保留旧包；临时文件避免失败时留下看似完整的 ZIP。
$archive = Join-Path $PSScriptRoot ('VideoPlayer-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.zip')
$temporaryArchive = $archive + '.tmp'
try {
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $output,
        $temporaryArchive,
        [IO.Compression.CompressionLevel]::Optimal,
        $true)
    Move-Item -LiteralPath $temporaryArchive -Destination $archive
} finally {
    if (Test-Path -LiteralPath $temporaryArchive -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryArchive -Force
    }
}

$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
    $archive + '.sha256',
    $hash + '  ' + [IO.Path]::GetFileName($archive) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Host "运行程序：$executable"
Write-Host "发布压缩包：$archive"
Write-Host "SHA256：$hash"
