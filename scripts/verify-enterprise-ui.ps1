param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$sdk = (Get-Command dotnet -ErrorAction Stop).Source
$project = Join-Path $repoRoot "src\CustomsClearanceConsole\CustomsClearanceConsole.csproj"
$output = Join-Path $repoRoot "artifacts\enterprise-ui-validation"
$previousData = $env:CUSTOMS_CONSOLE_DATA
New-Item -ItemType Directory -Force -Path $output | Out-Null
try {
    & $sdk run --project (Join-Path $repoRoot "tests\CoreRegression") -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "核心回归失败" }
    & $sdk build $project -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Windows 程序编译失败" }
    $app = Join-Path $repoRoot "src\CustomsClearanceConsole\bin\$Configuration\net8.0-windows\关单核验台.dll"
    $env:CUSTOMS_CONSOLE_DATA = Join-Path $output "test-data"
    & $sdk $app --ui-contract-self-test
    if ($LASTEXITCODE -ne 0) { throw "原生 UI 契约检查失败" }
    $stateFolder = Join-Path $env:CUSTOMS_CONSOLE_DATA "关单核验台数据"
    New-Item -ItemType Directory -Force -Path $stateFolder | Out-Null
    $records = @(
        @{ DeclarationNo="310120260000000001";SourcePath="sample.pdf";Consignee="NORTH STAR TRADING COMPANY LIMITED";ContractNo="EXPORT-2026-091";ExitCustoms="上海海关";DestinationCountry="美国";Status="需关注";Warning="金额冲突";Confidence=80;Totals=@{USD=1234567.89;CNY=23000} },
        @{ DeclarationNo="310120260000000001";SourcePath="duplicate.pdf";Consignee="NORTH STAR TRADING COMPANY LIMITED";ContractNo="EXPORT-2026-091";ExitCustoms="上海海关";DestinationCountry="美国";Status="双引擎校验通过";Confidence=90;Totals=@{USD=1234567.89} },
        @{ DeclarationNo="310120260000000003";SourcePath="third.pdf";Consignee="SAMPLE COMPANY";ContractNo="EXPORT-2026-092";ExitCustoms="上海海关";DestinationCountry="英国";Status="双引擎校验通过";Confidence=92;Totals=@{GBP=5500} }
    )
    @{UiSchemaVersion=5;PageSize=50;LastFolder="C:\业务资料\待识别关单";ScreenshotFolder="C:\业务资料\核验截图";Records=$records} | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $stateFolder "history.json")
    & $sdk $app --ui-snapshot (Join-Path $output "workspace-1600.png") 1600 1000
    if ($LASTEXITCODE -ne 0) { throw "宽屏截图失败" }
    & $sdk $app --ui-snapshot (Join-Path $output "workspace-1200.png") 1200 800
    if ($LASTEXITCODE -ne 0) { throw "窄屏截图失败" }
    foreach ($kind in @("directory", "cleanup", "verification")) {
        & $sdk $app --ui-dialog-snapshot (Join-Path $output "$kind.png") $kind
        if ($LASTEXITCODE -ne 0) { throw "弹窗截图失败：$kind" }
    }
    Write-Host "原生截图已保存：$output"
    Write-Host "请检查 100%、125%、150% 缩放，并使用真实样本完成 OCR、取消和截图联调。"
}
finally { $env:CUSTOMS_CONSOLE_DATA = $previousData }
