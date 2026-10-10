param(
    [string]$FontsDirectory = "",
    [string]$CacheDirectory = "",
    [string]$Python = "python"
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $FontsDirectory) { $FontsDirectory = Join-Path $repoRoot 'src\CustomsClearanceConsole\fonts' }
if (-not $CacheDirectory) {
    $base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
    $CacheDirectory = Join-Path $base 'ccc-font-sources'
}
New-Item -ItemType Directory -Force -Path $FontsDirectory, $CacheDirectory | Out-Null

$sources = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'font-sources.json') -Raw | ConvertFrom-Json

function Get-Verified {
    param([string]$Url, [string]$Target, [string]$Expected, [string]$Label)
    if (Test-Path -LiteralPath $Target) {
        $existing = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($existing -eq $Expected) { Write-Host "$Label 已缓存并通过 SHA256 校验。"; return }
        Remove-Item -LiteralPath $Target -Force
    }
    Write-Host "$Label 下载：$Url"
    Invoke-WebRequest -Uri $Url -OutFile $Target -UseBasicParsing
    $actual = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected) {
        throw "$Label SHA256 不一致。期望 $Expected，实际 $actual"
    }
    Write-Host "$Label SHA256 校验通过：$actual"
}

# ---- Noto Sans SC: official variable font + OFL, then static instancing ----
$noto = $sources.notoSansScVariable
$notoVariable = Join-Path $CacheDirectory 'NotoSansSC-Variable.ttf'
$notoLicense = Join-Path $CacheDirectory 'NotoSansSC-OFL.txt'
Get-Verified -Url $noto.url -Target $notoVariable -Expected $noto.sha256 -Label 'Noto Sans SC 变量字体'
Get-Verified -Url $noto.licenseUrl -Target $notoLicense -Expected $noto.licenseSha256 -Label 'Noto Sans SC OFL'

$instancesManifest = Join-Path $CacheDirectory 'noto-static-manifest.json'
& $Python (Join-Path $PSScriptRoot 'instantiate_noto.py') --variable $notoVariable --output $FontsDirectory --manifest $instancesManifest
if ($LASTEXITCODE -ne 0) { throw "Noto 静态字重实例化失败：$LASTEXITCODE" }

# ---- JetBrains Mono: official static release + OFL ----
$jetbrains = $sources.jetBrainsMono
$jbZip = Join-Path $CacheDirectory "JetBrainsMono-$($jetbrains.version).zip"
Get-Verified -Url $jetbrains.url -Target $jbZip -Expected $jetbrains.sha256 -Label 'JetBrains Mono 发布包'
$jbExtract = Join-Path $CacheDirectory "JetBrainsMono-$($jetbrains.version)"
if (Test-Path -LiteralPath $jbExtract) { Remove-Item -LiteralPath $jbExtract -Recurse -Force }
Expand-Archive -LiteralPath $jbZip -DestinationPath $jbExtract -Force
foreach ($member in @('JetBrainsMono-Regular.ttf', 'JetBrainsMono-Medium.ttf', 'JetBrainsMono-Bold.ttf')) {
    $source = Join-Path $jbExtract "fonts\ttf\$member"
    if (-not (Test-Path -LiteralPath $source)) { throw "JetBrains 发布包缺少：$member" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $FontsDirectory $member) -Force
}
Copy-Item -LiteralPath (Join-Path $jbExtract 'OFL.txt') -Destination (Join-Path $FontsDirectory 'JetBrainsMono-OFL.txt') -Force
Copy-Item -LiteralPath $notoLicense -Destination (Join-Path $FontsDirectory 'NotoSansSC-OFL.txt') -Force

# ---- Verify every shipped static font is static and carries the requested weight ----
$verifyScript = Join-Path $CacheDirectory 'verify-fonts.py'
@'
import json, os, sys
from fontTools.ttLib import TTFont
fonts = sys.argv[1]
expected = {
    "NotoSansSC-Regular.ttf": 400, "NotoSansSC-Medium.ttf": 500, "NotoSansSC-Bold.ttf": 700,
    "JetBrainsMono-Regular.ttf": 400, "JetBrainsMono-Medium.ttf": 500, "JetBrainsMono-Bold.ttf": 700,
}
report = []
for name, weight in expected.items():
    path = os.path.join(fonts, name)
    if not os.path.isfile(path):
        raise SystemExit(f"missing font: {path}")
    font = TTFont(path)
    try:
        if "fvar" in font:
            raise SystemExit(f"{name}: still variable")
        actual = font["OS/2"].usWeightClass
        if actual != weight:
            raise SystemExit(f"{name}: usWeightClass={actual} expected {weight}")
        family = font["name"].getDebugName(1)
        if family is None or "Source" in family:
            raise SystemExit(f"{name}: bad family name {family!r}")
        report.append({"file": name, "usWeightClass": actual, "family": family, "bytes": os.path.getsize(path)})
    finally:
        font.close()
print(json.dumps(report, ensure_ascii=False, indent=2))
'@ | Set-Content -LiteralPath $verifyScript -Encoding UTF8
& $Python $verifyScript $FontsDirectory
if ($LASTEXITCODE -ne 0) { throw "静态字体校验失败：$LASTEXITCODE" }

$manifest = [ordered]@{
    generatedAt = (Get-Date).ToString('s')
    notoSansSc = [ordered]@{
        sourceUrl = $noto.url
        sha256 = $noto.sha256
        licenseFile = $noto.licenseFile
        licenseSha256 = $noto.licenseSha256
        reservedFontName = $noto.reservedFontName
        instances = (Get-Content -LiteralPath $instancesManifest -Raw | ConvertFrom-Json).instances
    }
    jetBrainsMono = [ordered]@{
        version = $jetbrains.version
        sourceUrl = $jetbrains.url
        sha256 = $jetbrains.sha256
        licenseFile = $jetbrains.licenseFile
        reservedFontName = $jetbrains.reservedFontName
    }
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $FontsDirectory 'MANIFEST.json') -Encoding UTF8
Write-Host "静态字体与原始许可已写入：$FontsDirectory"
Get-ChildItem -LiteralPath $FontsDirectory | Select-Object Name, Length | Format-Table | Out-String | Write-Host
