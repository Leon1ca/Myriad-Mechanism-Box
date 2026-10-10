param(
    [string]$Configuration = "Release",
    [string]$LocalRoot = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $LocalRoot) { $LocalRoot = Join-Path $repoRoot "artifacts\local-current" }
$localRoot = [System.IO.Path]::GetFullPath($LocalRoot)
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts")) + [System.IO.Path]::DirectorySeparatorChar
if (-not $localRoot.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "本地运行目录必须位于仓库 artifacts 子目录内。"
}
$portableSdk = Join-Path $repoRoot ".devtools\dotnet-sdk\dotnet.exe"
$sdkCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$sdk = if (Test-Path -LiteralPath $portableSdk) { $portableSdk } elseif ($sdkCommand) { $sdkCommand.Source } else { $null }
$project = Join-Path $repoRoot "src\CustomsClearanceConsole\CustomsClearanceConsole.csproj"
$nugetConfig = Join-Path $repoRoot "NuGet.Config"
$assetsFile = Join-Path $repoRoot "src\CustomsClearanceConsole\obj\project.assets.json"
$buildOutput = Join-Path $repoRoot "src\CustomsClearanceConsole\bin\$Configuration\net8.0-windows"
$localApp = Join-Path $localRoot "app"
$runtime = Join-Path $localRoot "runtime\dotnet.exe"
$appDll = Join-Path $localApp "关单核验台.dll"
$launcherBuildScript = Join-Path $repoRoot "scripts\build-launcher.ps1"
$launcherOutput = Join-Path $repoRoot "artifacts\launcher\关单核验台.exe"
$usageGuide = Join-Path $repoRoot "src\CustomsClearanceConsole\使用说明.txt"
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
$version = [string]($projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "项目版本号格式无效：$version" }
$notesName = "更新说明-v$version.md"
$releaseNotes = Join-Path $repoRoot "docs\release-notes-v$version.md"

if (-not $sdk) {
    throw "未找到 .NET 8 SDK。请安装 .NET 8 SDK，或放到仓库 .devtools\dotnet-sdk。"
}

foreach ($required in @($project, $nugetConfig, $localRoot, $localApp, $runtime, $releaseNotes)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "缺少本地开发依赖：$required"
    }
}

$devState = Join-Path $repoRoot "artifacts\dev-state"
$cliHome = Join-Path $devState "dotnet-cli-home"
$buildTemp = Join-Path $devState "temp"
foreach ($directory in @($cliHome, $buildTemp)) {
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
}

$env:DOTNET_CLI_HOME = $cliHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:TEMP = $buildTemp
$env:TMP = $buildTemp

Push-Location $repoRoot
try {
    if (-not (Test-Path -LiteralPath $assetsFile)) {
        & $sdk restore $project --configfile $nugetConfig -v:q
        if ($LASTEXITCODE -ne 0) { throw "程序依赖准备失败，退出代码：$LASTEXITCODE" }
    }

    & $sdk run --project (Join-Path $repoRoot "tests\CoreRegression\CoreRegression.csproj") -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "核心业务回归测试失败，退出代码：$LASTEXITCODE" }

    & $sdk build $project -c $Configuration --no-restore -v:q
    if ($LASTEXITCODE -ne 0) { throw "程序构建失败，退出代码：$LASTEXITCODE" }

    try {
        Copy-Item -Path (Join-Path $buildOutput "*") -Destination $localApp -Recurse -Force
    }
    catch [System.UnauthorizedAccessException] {
        throw "本地程序文件正在使用。请先关闭关单核验台，再重新运行同步。"
    }

    & $runtime $appDll --ui-contract-self-test
    if ($LASTEXITCODE -ne 0) { throw "本地回归测试失败，退出代码：$LASTEXITCODE" }

    & $launcherBuildScript
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $launcherOutput)) {
        throw "启动器构建失败，退出代码：$LASTEXITCODE"
    }
    Copy-Item -LiteralPath $launcherOutput -Destination (Join-Path $localRoot "关单核验台.exe") -Force
    Copy-Item -LiteralPath $usageGuide -Destination (Join-Path $localRoot "使用说明.txt") -Force
    Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $localRoot $notesName) -Force
    Get-ChildItem -LiteralPath $localRoot -Filter "更新说明-v*.md" -File |
        Where-Object { $_.Name -ne $notesName } |
        Remove-Item -Force

    Write-Host "仓库内本地运行版已更新：$localRoot"
    Write-Host "本次迭代未创建 ZIP，也未复制 OCR 模型或 .NET 运行时。"
}
finally {
    Pop-Location
}
