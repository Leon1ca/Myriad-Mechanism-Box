$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '找不到 Windows C# 编译器。' }

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$launcherRoot = Join-Path $repoRoot 'src\Launcher'
$icon = Join-Path $repoRoot 'src\CustomsClearanceConsole\Assets\app.ico'
$outputRoot = Join-Path $repoRoot 'artifacts\launcher'
New-Item -ItemType Directory -Force $outputRoot | Out-Null
$output = Join-Path $outputRoot '关单核验台.exe'

& $compiler '/nologo' '/target:winexe' '/optimize+' '/platform:anycpu' "/win32icon:$icon" "/win32manifest:$(Join-Path $launcherRoot 'app.manifest')" '/reference:System.dll' '/reference:System.Core.dll' '/reference:System.Windows.Forms.dll' "/out:$output" (Join-Path $launcherRoot 'Program.cs') (Join-Path $launcherRoot 'AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "启动器已生成：$output"

