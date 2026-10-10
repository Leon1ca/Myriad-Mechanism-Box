# 随包字体与许可

程序启动时 `AppFonts.Initialize()` 从 `fonts/` 目录加载以下**静态**字体；`AppFonts.VerifyShipset()` 在云端环境报告中验证字族与字重真实解析。正式便携包必须包含全部静态字体与原始许可，缺失即视为打包失败。

| 文件 | 字体 / 字重 | 来源（官方固定版本） | 许可 |
|---|---|---|---|
| `NotoSansSC-Regular.ttf` | Noto Sans SC Regular（wght 400，静态） | google/fonts `2894aab3` · `ofl/notosanssc/NotoSansSC[wght].ttf` | SIL OFL 1.1（`NotoSansSC-OFL.txt`） |
| `NotoSansSC-Medium.ttf` | Noto Sans SC Medium（wght 500，静态） | 同上，`fontTools.varLib.instancer` 实例化 | 同上 |
| `NotoSansSC-Bold.ttf` | Noto Sans SC Bold（wght 700，静态） | 同上，实例化 | 同上 |
| `JetBrainsMono-Regular.ttf` | JetBrains Mono Regular | JetBrains/JetBrainsMono `v2.304` 发布包 `fonts/ttf/` | SIL OFL 1.1（`JetBrainsMono-OFL.txt`） |
| `JetBrainsMono-Medium.ttf` | JetBrains Mono Medium | 同上 | 同上 |
| `JetBrainsMono-Bold.ttf` | JetBrains Mono Bold | 同上 | 同上 |

## 生成与校验（云端）

`scripts/prepare-fonts.ps1` 在 GitHub Actions 中执行：

1. 从固定提交 `google/fonts@2894aab31764f10f29c421bdfd2340d3b382d384` 下载 `NotoSansSC[wght].ttf`，校验 SHA256
   `a3041811a78c361b1de50f953c805e0244951c21c5bd412f7232ef0d899af0da`。
2. 下载同提交的 `OFL.txt`，校验 SHA256
   `1c05c68c34f9708415aada51f17e1b0092d2cea709bf4a94cd38114f9e73d7d9`。
3. 用 `fontTools.varLib.instancer` 在 wght 400 / 500 / 700 实例化为静态 TTF，并校验生成文件无 `fvar`、`OS/2.usWeightClass` 等于目标字重。
4. 下载 `JetBrainsMono-2.304.zip`，校验 SHA256
   `6f6376c6ed2960ea8a963cd7387ec9d76e3f629125bc33d1fdcd7eb7012f7bbf`，解出三个静态 TTF 与 `OFL.txt`。
5. 写入 `MANIFEST.json`（来源 URL、SHA256、实例化字重）供打包校验。

不提交任何 `.ttf` 二进制（见 `.gitignore`）；原始 OFL 文本随仓库保留并在云端用官方下载覆盖。

## OFL 保留字名说明

Noto Sans SC 的上游 OFL 标注 `with Reserved Font Name 'Source'`。生成的静态实例保留字族名 `Noto Sans SC`，其中**不包含**保留字名 `Source`，符合 OFL 对保留字名的限制；未把字族改名为含 `Source` 的任何形式。JetBrains Mono 的 OFL 未声明保留字名。

## 加载与验证

- `AppFonts` 只加载上述静态文件；`VerifyShipset()` 断言 `Noto Sans SC` / `Noto Sans SC Medium` / `JetBrains Mono` / `JetBrains Mono Medium` 字族存在，且 Bold 请求解析为真实粗体。
- 许可原文随包放在同目录，便携包内路径为 `app/fonts/`；`LICENSE`、`third-party-notices/` 与 `AboutAndLicenses.txt` 同时随根目录与 `app/` 提供。
