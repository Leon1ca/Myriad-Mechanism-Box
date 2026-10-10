# 千机匣 · Myriad Mechanism Box

千机匣提供 Windows 10 / 11 x64 本地关单识别与核验工具，当前桌面模块为“关单核验台”。当前正式初始版本为 **v0.1.0**，发布日期 **2026-10-10**。以经确认的 1.5.3 源码为基础，统一版本编号并优化常见工作关单的识别规则。

[下载便携版](https://github.com/Leon1ca/Myriad-Mechanism-Box/releases/tag/v0.1.0) · [正式版本说明](docs/release-notes-v0.1.0.md)

![关单核验台主界面](docs/images/main-ui.png)

## 使用方式

完整解压发布包，双击根目录 `关单核验台.exe`。无需预装 .NET、Python、OCR 或数据库。在设置中选择关单和截图目录，点击“开始识别”。每批最多 200 个文件；超过上限请分批处理。

## 主要功能

- 支持 PDF、PNG、JPG/JPEG/JFIF、BMP、TIF/TIFF、GIF、WEBP；支持多页及横放、倒置页面。
- 提取报关单号、境外收货人、合同协议号、出境关别、目的国和逐币种总货值；提供商品名称及规格、计价数量、单位、单价和总价明细。
- 优先读取 PDF 文字层；扫描件使用 PP-OCRv5 本地识别，商品表缺项时按需局部补扫。
- 按实际印刷精度核对数量、单价和金额；检查表头号码及商品行完整性。异常项保留供人工核对，金额不符的项目不计入可信合计。
- 当前批次按报关单号检测重复，分别显示去重前和去重后多币种金额，不进行汇率换算。
- 支持搜索、状态筛选、分页、列宽调整、选中单元格复制和逐项明细查看。
- 导出 Excel 三张工作表或 Markdown，覆盖整批记录，包括筛选隐藏和其他分页中的记录。
- 用户逐条点击“校验”后打开单一窗口 pi4 查询页。人工输入验证码并查询，再点击网页卡片“长截图”保存结果。
- 列表、关单和截图分别清理；文件清理须两次确认，只处理当前层并移入回收站。

## 开发构建

要求 Windows x64 和 .NET 8 SDK。从便携包准备本地依赖：

```powershell
.\scripts\bootstrap-from-release.ps1 -PackageRoot 'D:\Downloads\MyriadMechanismBox-Windows-x64-v0.1.0'
dotnet build .\src\CustomsClearanceConsole\CustomsClearanceConsole.csproj -c Release
dotnet run --project .\tests\CoreRegression -c Release
dotnet run --project .\tests\TransportRegression -c Release
```

日常迭代和打包见[本地开发流程](docs/local-development-workflow.md)。识别规则见[OCR 流程](docs/ocr-pipeline.md)。代码分层见[架构设计](docs/architecture.md)，稳定性修复与验证见[整体代码审核](docs/code-review-v0.1.0.md)。本仓库以经过识别优化和整体代码审核的完整源码作为初始提交，公开发布版本从 0.1.0 开始。旧仓库历史另行本地归档。

## 数据与许可

关单内容、OCR 中间结果、识别历史和日志留在本机。只有用户点击“校验”时才访问单一窗口网站，验证码由用户完成。

项目按 GNU GPL v3 发布。第三方组件许可见 `third-party-notices` 和程序内 `AboutAndLicenses.txt`。
