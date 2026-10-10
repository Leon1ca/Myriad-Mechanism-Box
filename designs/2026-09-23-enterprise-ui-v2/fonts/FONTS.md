# 字体（开源，SIL Open Font License 1.1）

本包不附带字体文件本体（打包环境无网络）。请按下表下载，并把静态 TTF 放到便携版 `app\fonts\` 目录。

| 用途 | 字体 | 需要的文件 | 获取 |
|---|---|---|---|
| 界面文字（中文 + 西文） | Noto Sans SC（即思源黑体的 Google 发行版） | `NotoSansSC-Regular.ttf`、`NotoSansSC-Medium.ttf`、`NotoSansSC-Bold.ttf` | Google Fonts 下载 “Noto Sans SC” 字体包，取压缩包内 `static/` 目录 |
| 单号、金额、合同号、路径 | JetBrains Mono | `JetBrainsMono-Regular.ttf`、`JetBrainsMono-Medium.ttf` | JetBrains Mono 的 GitHub Releases 或 Google Fonts，取 `fonts/ttf/` 或 `static/` 目录 |

## 注意事项

- **用静态字重文件，不要用可变字体**（文件名带 `[wght]` 的那个）。GDI / GDI+ 对可变字体只取默认实例，Medium、Bold 会失效。
- **体积**：Noto Sans SC 每个静态字重约 8–10 MB，三个字重约 25–30 MB。如需压缩，可用 `pyftsubset`（fonttools）按 GB 2312 + 常用符号做子集化。子集化属于 OFL 意义上的修改：仍须附带 OFL 许可；能否沿用原字体名，以字体包内 `OFL.txt` 开头声明的“Reserved Font Name”为准，如有保留名就需要改名。
- **许可**：两款字体的发行包都自带 `OFL.txt`。把两份原文复制到 `third-party-notices/`（例如 `third-party-notices/NotoSansSC-OFL.txt`、`third-party-notices/JetBrainsMono-OFL.txt`），并在 `AboutAndLicenses.txt` 中列出。OFL 允许与 GPL v3 程序一起分发，限制是不得单独出售字体文件。
- **加载**：见 `code/AppFonts.cs`。缺少字体文件时程序会回退到系统字体，不会阻断启动。

## 设计稿与效果图中的字体

- 画布设计稿通过 Google Fonts 加载 Noto Sans SC 与 JetBrains Mono。
- `screenshots/` 中的效果图在离线环境渲染：中文使用同一字形的 Noto Sans CJK SC，**等宽数字临时用 DejaVu Sans Mono 代替**，所以数字字形与最终效果略有差异，尺寸与布局不受影响。
