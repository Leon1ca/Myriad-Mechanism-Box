# 便携浏览器 E2E（开发工具，不随包发布）

在 Linux / macOS / Windows 上直接运行**生产代码**里的受控长截图 E2E（`BrowserCaptureE2E.cs` + `BrowserValidation.cs` + `BrowserScripts/*.js`），不需要 WinForms，也不需要 Windows 云端。用于本地快速复现、定位间歇问题；**不能替代** `.github/workflows/windows-ui-v2.yml` 的 Windows/Edge 最终门禁。

`System.Drawing` 里仅用到的拼图、PNG 读写和取像素由 `DrawingShim.cs` 基于 SkiaSharp（MIT）提供，产品代码不做任何修改。

## 用法

```bash
# 任意 Chromium/Chrome/Edge 可执行文件。以 root 运行时需要一个加 --no-sandbox 的包装脚本：
printf '#!/bin/sh\nexec /path/to/chrome --no-sandbox "$@"\n' > /tmp/chrome && chmod +x /tmp/chrome
export CUSTOMS_CONSOLE_E2E_BROWSER=/tmp/chrome
export CUSTOMS_CONSOLE_DATA=/tmp/ccc-data                 # 浏览器临时配置等写到这里
export CUSTOMS_CONSOLE_APILOG=/tmp/ccc-e2e/app.log        # 生产日志（接受/拒绝/锁/诊断）

dotnet build tests/BrowserE2E.Portable -c Release
# 完整 30 个场景
dotnet tests/BrowserE2E.Portable/bin/Release/net8.0/BrowserE2E.Portable.dll /tmp/ccc-e2e
# 单个场景重复 N 次（场景名即 BrowserCaptureE2E 中的私有方法名）
dotnet tests/BrowserE2E.Portable/bin/Release/net8.0/BrowserE2E.Portable.dll /tmp/ccc-e2e RunOopifViewportReadFailureAsync 30
```

## 复现间歇问题

时序问题通常只在 CPU 紧张时出现（云端 runner 常见）。可在另一个终端占满所有核心后再循环运行单个场景，例如：

```bash
python3 -c "import multiprocessing as m
def f():
    while True: pass
[m.Process(target=f).start() for _ in range(m.cpu_count())]"
```

2026-09-24 用此方法在 Chromium 141 上复现了 `oopif-viewport-read-failure` 的历史云端失败（旧代码 15 次中失败 4 次），历史复现记录已本地归档；当前架构与验证说明见 `docs/architecture.md` 和 `docs/code-review-v0.1.0.md`。
