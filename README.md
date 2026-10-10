# 千机匣 · Myriad Mechanism Box

千机匣（Myriad Mechanism Box）是 Windows 10 / 11 x64 本地关单识别与核验工具，原名“关单核验台”。当前版本为 **v0.2.0**（2026-10-10），在 v0.1.0 基础上精简列表与明细、消除刷新闪屏并整体审查代码。

[下载便携版](https://github.com/Leon1ca/Myriad-Mechanism-Box/releases) · [v0.2.0 版本说明](docs/release-notes-v0.2.0.md) · [v0.1.0 版本说明](docs/release-notes-v0.1.0.md)

![千机匣主界面](docs/images/main-ui.png)

右上方为“设置”，工作区提供“导出列表”“开始识别”，记录区通过“清理”菜单执行三类清理。

## 使用方式

完整解压发布包，双击根目录 `千机匣.exe`。无需预装 .NET、Python、OCR 或数据库。

1. 点击标题栏右上方“设置”，选择关单目录和截图目录并保存。也可将 PDF / 图片拖入记录区，载入一个新批次。
2. 点击“开始识别”；识别期间按钮切换为“取消识别”。仅读取目录当前层，每批最多 200 个文件，超过上限请分批处理。
3. 使用“全部 / 正常 / 重复 / 需关注”筛选和搜索查看记录，点击“明细”查看商品名称、数量、单价和总价。左侧显示本批文件、重复单号、需关注和截图留存数量；右侧按币种显示金额汇总。
4. 点击“导出列表”选择 Excel 或 Markdown；记录区右上方“清理”菜单包含列表清理、关单清理和截图清理。

网页核验需安装 Edge 或 Chrome，核验窗口使用独立的专用浏览器配置（原因见界面说明）。点击记录的“校验”，在网页输入验证码并查询，再点击截图卡片的“长截图”保存结果。完整操作与边界见[界面与使用说明](docs/ui-and-boundaries.md)。

## 主要功能

- 支持 PDF、PNG、JPG/JPEG/JFIF、BMP、TIF/TIFF、GIF、WEBP；支持多页及横放、倒置页面。
- 提取报关单号、境外收货人、合同协议号、出境关别、目的国和逐币种总货值；明细列出商品名称、数量、单价和总价。
- 优先读取 PDF 文字层；扫描件使用 PP-OCRv5 本地识别，商品表缺项时按需局部补扫。
- 按实际印刷精度核对数量、单价和金额；检查表头号码及商品行完整性。异常项保留供人工核对，金额不符的项目不计入可信合计。
- 当前批次按报关单号检测重复，分别显示去重前和去重后多币种金额，不进行汇率换算。
- 支持搜索、状态筛选、分页、列宽调整、选中单元格复制和逐项明细查看。
- “导出列表”可导出 Excel 三张工作表或 Markdown，覆盖整批记录，包括筛选隐藏和其他分页中的记录。
- 用户逐条点击“校验”后打开单一窗口 pi4 查询页。人工输入验证码并查询，再点击网页卡片“长截图”保存结果。
- “清理”菜单分别提供列表、关单和截图清理；列表清理只删除记录，文件清理须两次确认，只处理当前层并移入回收站。

## 开发构建

要求 Windows x64 和 .NET 8 SDK。从便携包准备本地依赖：

```powershell
.\scripts\bootstrap-from-release.ps1 -PackageRoot 'D:\Downloads\MyriadMechanismBox-Windows-x64-v0.1.0'
dotnet build .\src\CustomsClearanceConsole\CustomsClearanceConsole.csproj -c Release
dotnet run --project .\tests\CoreRegression -c Release
dotnet run --project .\tests\TransportRegression -c Release
```

日常迭代和打包见[本地开发流程](docs/local-development-workflow.md)。识别规则见[OCR 流程](docs/ocr-pipeline.md)。代码分层、界面刷新机制和维护要点见[架构设计](docs/architecture.md)。

## 数据与许可

关单内容、OCR 中间结果、识别历史和日志留在本机。只有用户点击“校验”时才访问单一窗口网站，验证码由用户完成。

项目按 GNU GPL v3 发布。第三方组件许可见 `third-party-notices` 和程序内 `AboutAndLicenses.txt`。
