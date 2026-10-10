# 千机匣维护交接

更新：2026-10-10。当前正式版本序列从 v0.1.0 开始，源码及界面以已确认的 1.5.3 为基线。当前功能、验证范围和限制见[正式版本说明](release-notes-v0.1.0.md)与[工作关单识别验证](recognition-validation.md)。

## 构建与发布

主程序位于 `src/CustomsClearanceConsole`，启动器位于 `src/Launcher`。使用 .NET 8 SDK；从完整 v0.1.0 便携包运行 `scripts/bootstrap-from-release.ps1` 准备 PP-OCRv5、原生组件与 PDFium。字体由 `scripts/prepare-fonts.ps1` 准备并验证。具体步骤见[本地开发流程](local-development-workflow.md)。

源码目录与资源所有权见[架构设计](architecture.md)，本次修复证据见[整体代码审核](code-review-v0.1.0.md)。

`tests/CoreRegression` 包含 102 项合成或脱敏回归，`tests/TransportRegression` 包含 8 项真实 WebSocket 通信检查。命令行自检包含 `--resource-self-test`（7 项原生资源检查）、`--ocr-self-test`、`--browser-e2e`、`--ui-contract-self-test`、`--export-samples`、`--env-report`。排查单据可用 `--ocr-debug <文件>`，批次回放用 `--self-test <目录>`。真实样本、逐文件结果和参考值不提交公开仓库。

Windows 发布工作流先执行核心、OCR、界面、浏览器、导出、字体、打包与解压门禁，再发布同一验证包。在 main 手动指定与项目版本一致且尚未发布的标签，发布通过门禁的同一包。构建依赖始终从本仓库 v0.1.0 发布包取得，旧仓库删除后仍可重建。

## 识别规则

PDF 文字层与 OCR 都执行金额一致性和漏项检查。计价数量及单位从原页同一文字组合选取，不能由总价倒算。舍入容差随单价印刷小数位变化；整数单价不额外放宽。斜向大水印不作为表格行文字。局部补扫只补缺失文字，不覆盖已有冲突值。详情见[OCR 流程](ocr-pipeline.md)。

本次 220 份有效关单的 787 个商品项在报告列出的字段全匹配。商品名称及规格、出境关别和目的国没有完整独立标注；不能把该结果理解为所有字段或所有未知版式均准确。

## 维护要点与待验证事项

- DPI 按 96 DPI 逻辑尺寸搭建，交给 DpiLayout / DpiDialog 缩放；不要在构造期间重复缩放。
- 表格每格重置 SmoothingMode / PixelOffsetMode，避免圆角绘制状态产生灰色接缝。
- 弹窗边框统一用 PopupFrame，避免阴影或抗锯齿区域造成边框不均。
- CDP 的 URL 片段来自 urlFragment，页内路由切换需要监听 navigatedWithinDocument。官网查询框架异步加载，卡片只安装在顶层。
- 浏览器截图含 60 秒整体上限、卡片看门狗和逐步日志。受控仿真测试不能替代真实单一窗口网站验收；真实“截取中”问题仍需用户完成验证码后复现，并查看截图步骤日志定位。
- 严重倾斜、模糊、裁切和遮挡仍需人工核对；出境关别依赖可读取的原页文字及有限关区别名。
- 历史记录需要重新识别才能应用新规则。旧双引擎执行及合并代码已删除，仅保留旧历史复核字段的兼容读写，当前仅运行 PP-OCRv5。
- 新仓库从审核后的 v0.1.0 源码快照开始；旧仓库历史和过期迭代文档另行本地归档，不带入新仓库。
