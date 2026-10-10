param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot
)

$ErrorActionPreference = 'Stop'
$resolvedPackage = (Resolve-Path -LiteralPath $PackageRoot).Path
$appSource = Join-Path $resolvedPackage 'app'
$toolsSource = Join-Path $resolvedPackage 'tools'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\src\CustomsClearanceConsole')).Path

if (-not (Test-Path -LiteralPath (Join-Path $appSource '关单核验台.dll'))) {
    throw "PackageRoot 不是完整的关单核验台解压目录：$resolvedPackage"
}
if (-not (Test-Path -LiteralPath $toolsSource)) {
    throw "发布包缺少 tools 目录：$toolsSource"
}

$binaryTarget = Join-Path $projectRoot 'dual-ocr-bin'
$modelTarget = Join-Path $projectRoot 'ocr-models'
$toolsTarget = Join-Path $projectRoot 'tools'
New-Item -ItemType Directory -Force $binaryTarget, $modelTarget, $toolsTarget | Out-Null

$binaryNames = @(
    'Clipper2Lib.dll', 'Emgu.CV.dll', 'Microsoft.ML.OnnxRuntime.dll', 'System.Numerics.Tensors.dll',
    'cvextern.dll', 'onnxruntime.dll', 'onnxruntime_providers_shared.dll', 'opencv_videoio_ffmpeg4120_64.dll',
    'concrt140.dll', 'libusb-1.0.dll', 'msvcp140.dll', 'msvcp140_1.dll', 'msvcp140_2.dll',
    'msvcp140_atomic_wait.dll', 'msvcp140_codecvt_ids.dll', 'vcruntime140.dll', 'vcruntime140_1.dll'
)

foreach ($name in $binaryNames) {
    $source = Join-Path $appSource $name
    if (-not (Test-Path -LiteralPath $source)) { throw "发布包缺少构建依赖：$name" }
    Copy-Item -LiteralPath $source -Destination $binaryTarget -Force
}

Get-ChildItem -LiteralPath (Join-Path $appSource 'ocr-models') -Force | Copy-Item -Destination $modelTarget -Recurse -Force
# Since v1.5.3 the only OCR engine is PP-OCRv5 (ocr-models); older packages still carry Tesseract.
Get-ChildItem -LiteralPath $toolsSource -Force | Where-Object { $_.Name -ne 'tesseract' } |
    Copy-Item -Destination $toolsTarget -Recurse -Force

Write-Host '开发依赖已准备完成。现在可以执行：'
Write-Host 'dotnet build .\src\CustomsClearanceConsole\CustomsClearanceConsole.csproj -c Release'
