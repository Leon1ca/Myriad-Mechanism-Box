param([string]$SourceRoot = "")

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $SourceRoot) { $SourceRoot = Join-Path $repoRoot 'artifacts\local-current' }
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$artifacts = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $SourceRoot.StartsWith($artifacts, [StringComparison]::OrdinalIgnoreCase)) {
    throw '只能打包仓库 artifacts 内的运行目录。'
}
$project = [xml](Get-Content -LiteralPath (Join-Path $repoRoot 'src\CustomsClearanceConsole\CustomsClearanceConsole.csproj') -Raw)
$version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '项目版本无效。' }
foreach ($relative in @('关单核验台.exe', 'app\关单核验台.dll')) {
    $file = Get-Item -LiteralPath (Join-Path $SourceRoot $relative)
    if ($file.VersionInfo.FileVersion -ne "$version.0") { throw "版本不一致：$relative" }
}
if (Test-Path -LiteralPath (Join-Path $SourceRoot 'tools\tesseract')) { throw '发布包不应再包含 Tesseract（v1.5.3 起只用 PP-OCRv5）。' }
foreach ($relative in @('runtime\dotnet.exe', 'tools\pdfium\pdfium.dll', "更新说明-v$version.md", 'app\ocr-models\ppocrv5_dict.txt',
        'LICENSE', 'AboutAndLicenses.txt', 'third-party-notices\ONNXRuntime-MIT.txt', 'third-party-notices\EmguCV-GPLv3-or-Commercial.txt',
        'app\AboutAndLicenses.txt', 'app\fonts\NotoSansSC-Regular.ttf', 'app\fonts\NotoSansSC-Medium.ttf', 'app\fonts\NotoSansSC-Bold.ttf',
        'app\fonts\JetBrainsMono-Regular.ttf', 'app\fonts\JetBrainsMono-Medium.ttf', 'app\fonts\JetBrainsMono-Bold.ttf',
        'app\fonts\NotoSansSC-OFL.txt', 'app\fonts\JetBrainsMono-OFL.txt', 'app\fonts\MANIFEST.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot $relative))) { throw "发布包缺少：$relative" }
}
if (Test-Path -LiteralPath (Join-Path $SourceRoot 'app\fonts\NotoSansSC-Variable.ttf')) {
    throw '发布包仍包含可变字体，违反静态字体要求。'
}

$outputRoot = Join-Path $repoRoot "artifacts\releases\v$version"
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$zipPath = Join-Path $outputRoot "关单核验台-Windows-x64-v$version.zip"
if (Test-Path -LiteralPath $zipPath) { throw "已有发布包，不覆盖：$zipPath" }
$temporary = Join-Path $outputRoot ([Guid]::NewGuid().ToString('N') + '.tmp')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$excluded = 0
$included = 0
try {
    $archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $SourceRoot -Recurse -File | Sort-Object FullName)) {
            $relative = $file.FullName.Substring($SourceRoot.Length + 1)
            # Exclude only verified redundant nested OCR models; keep originals on disk.
            if ($relative.StartsWith('app\ocr-models\ocr-models\', [StringComparison]::OrdinalIgnoreCase)) {
                $original = Join-Path $SourceRoot ($relative.Replace('ocr-models\ocr-models\', 'ocr-models\'))
                if (-not (Test-Path -LiteralPath $original) -or
                    (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $original).Hash) {
                    throw "嵌套模型不是相同副本，停止打包：$relative"
                }
                $excluded++
                continue
            }
            if ($file.Extension -in @('.pdf', '.jpg', '.jpeg', '.log') -or $file.Name -match '^(history|state|appsettings)\.json$') {
                throw "发现非发布用数据，需要人工审查：$relative"
            }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName,
                $relative.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            $included++
        }
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $zipPath
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $outputRoot 'SHA256SUMS.txt'), "$hash  $([IO.Path]::GetFileName($zipPath))`n", [Text.UTF8Encoding]::new($false))
Write-Host "打包完成：$zipPath"
Write-Host "包含 $included 个文件，排除 $excluded 个已核对的重复模型；原文件未删除。"
Write-Host "SHA256: $hash"
