#Requires -Version 7
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$automation = Join-Path $root 'artifacts/publish/Publish.ps1'
if (-not (Test-Path -LiteralPath $automation -PathType Leaf)) {
    throw '一键发布入口不存在。'
}
$testRoot = Join-Path $root ('artifacts/publish-tests/' + [Guid]::NewGuid().ToString('N'))

function New-PublishFixture {
    param([string]$Name, [string]$Publisher)

    $fixture = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path (Join-Path $fixture 'artifacts/publish') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts/acceptance') -Force | Out-Null
    Copy-Item -LiteralPath $automation -Destination (Join-Path $fixture 'artifacts/publish/Publish.ps1')
    Set-Content -LiteralPath (Join-Path $fixture 'scripts/acceptance/Publish-Release.ps1') -Value $Publisher -Encoding utf8
    return $fixture
}

# 隔离耗时的 dotnet 发布边界，实际执行入口、ZIP 打包、校验文件生成及失败传播。
$success = New-PublishFixture '中文 路径 success' @'
$output = Join-Path $PSScriptRoot '../../artifacts/publish/win-x64'
New-Item -ItemType Directory -Path (Join-Path $output 'libvlc/win-x64/plugins') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $output 'LICENSES') -Force | Out-Null
[IO.File]::WriteAllBytes((Join-Path $output 'VideoPlayer.exe'), [byte[]](77, 90, 0, 1))
Set-Content -LiteralPath (Join-Path $output 'libvlc/win-x64/plugins/sample.dll') -Value 'plugin'
Set-Content -LiteralPath (Join-Path $output 'LICENSES/NOTICE.txt') -Value 'license'
'@
foreach ($run in 1..2) {
    # 从仓库外启动，验证入口不依赖当前工作目录；再次运行应保留旧包。
    Push-Location ([IO.Path]::GetPathRoot($root))
    try {
        $console = & pwsh -NoProfile -File (Join-Path $success 'artifacts/publish/Publish.ps1') 2>&1
        $result = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($result -ne 0) {
        throw "成功场景运行失败：$console"
    }
    $archives = @(Get-ChildItem -LiteralPath (Join-Path $success 'artifacts/publish') -Filter '*.zip' -File)
    if ($archives.Count -ne $run) {
        throw '重复发布没有保留独立压缩包。'
    }
}
foreach ($archive in $archives) {
    $zip = [IO.Compression.ZipFile]::OpenRead($archive.FullName)
    try {
        foreach ($entry in @('win-x64/VideoPlayer.exe', 'win-x64/libvlc/win-x64/plugins/sample.dll', 'win-x64/LICENSES/NOTICE.txt')) {
            if ($null -eq $zip.GetEntry($entry)) {
                throw "压缩包丢失必要文件：$entry"
            }
        }
        $stream = $zip.GetEntry('win-x64/VideoPlayer.exe').Open()
        try {
            if ($stream.ReadByte() -ne 77 -or $stream.ReadByte() -ne 90) {
                throw '压缩包中的程序内容不一致。'
            }
        } finally {
            $stream.Dispose()
        }
    } finally {
        $zip.Dispose()
    }
    $expectedHash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash
    $checksum = Get-Content -LiteralPath ($archive.FullName + '.sha256') -Raw
    if (-not $checksum.StartsWith($expectedHash, [StringComparison]::OrdinalIgnoreCase) -or
        -not $checksum.Contains($archive.Name)) {
        throw 'SHA256 校验文件与压缩包不匹配。'
    }
}
Write-Host 'PASS: 发布打包、中文空格路径、仓库外调用、重复发布及 SHA256。'

foreach ($case in @(
    @{ Name = 'publish-failed'; Publisher = 'exit 23'; ExpectedExit = 23 },
    @{ Name = 'missing-exe'; Publisher = 'exit 0'; ExpectedExit = 1 }
)) {
    $fixture = New-PublishFixture $case.Name $case.Publisher
    # 失败发布时，即使旧输出存在，也不能误打包为本次成功产物。
    if ($case.Name -eq 'publish-failed') {
        $staleOutput = Join-Path $fixture 'artifacts/publish/win-x64'
        New-Item -ItemType Directory -Path $staleOutput -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $staleOutput 'VideoPlayer.exe') -Value 'old executable'
    }
    $console = & pwsh -NoProfile -File (Join-Path $fixture 'artifacts/publish/Publish.ps1') 2>&1
    if ($LASTEXITCODE -ne $case.ExpectedExit) {
        throw "$($case.Name) 未正确返回失败：$console"
    }
    if (@(Get-ChildItem -LiteralPath (Join-Path $fixture 'artifacts/publish') -Filter '*.zip' -File).Count -ne 0) {
        throw "$($case.Name) 仍生成了压缩包。"
    }
    Write-Host "PASS: $($case.Name) 停止打包并返回失败。"
}
Write-Host "Publish automation tests passed. Evidence: $testRoot"
