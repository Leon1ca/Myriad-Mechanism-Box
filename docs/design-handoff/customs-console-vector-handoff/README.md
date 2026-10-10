# 关单核验台 UI 交付包

这是一套可继续编辑、也可直接交给 AI 或程序工程师实现功能的界面源稿。设计基准尺寸为 `1586 × 992`，采用 Material 3 语义色、8px 间距节奏，并针对 Windows 企业桌面工具重新调整了标题栏、命令栏、统计总览和高密度表格。

## 文件说明

| 文件 | 用途 |
| --- | --- |
| `assets/app-icon.svg` | 独立软件图标矢量源稿，所有徽记结构均为可编辑路径 |
| `assets/app-icon.ico` | Windows 多分辨率软件图标，可直接用于打包 |
| `assets/icons/` | 16、20、24、32、40、48、64、128、256px 的像素对齐 PNG 图标，避免运行时缩放失真 |
| `assets/button-icons/` | 五枚独立绘制的专用按钮 SVG：目录设置、关单清理、截图清理、开始识别、列表清理 |
| `design/customs-console-ui.svg` | 主界面分层矢量源稿，文字保持可编辑，组件带有语义 ID 与 `data-action` |
| `design/button-icon-spec.svg` | 按钮图标放大检视稿，标明每枚图标的具体业务语义 |
| `design/interaction-states.svg` | 目录设置、清理确认、记录筛选和每页显示的弹窗/展开状态 |
| `prototype/index.html` | 可运行的语义化交互原型 |
| `prototype/styles.css` | Material 3 视觉样式和响应式布局 |
| `prototype/app.js` | 目录设置、确认弹窗、下拉框、开始识别等交互示例 |
| `design-tokens.json` | 颜色、字号、间距、圆角、阴影和组件状态令牌 |
| `AI_UI_BUILD_SPEC.md` | 可直接交给编码 AI 的单文件完整制作规格，包含布局坐标、色彩、字体、组件、交互、数据与验收参数 |
| `AI_IMPLEMENTATION_PROMPT.md` | 可直接复制给编码 AI 的实现任务说明 |
| `preview/main-interface.png`、`preview/button-icons.png` | 主界面与专用按钮图标的直接效果预览 |

## 快速预览

若要交给 AI 制作，请优先提供 `AI_UI_BUILD_SPEC.md`，并将整个交付目录作为附件。该文件是参数冲突时的最高优先级真源。

直接打开 `prototype/index.html` 即可查看；如果浏览器限制本地资源，可在本目录启动任意静态服务器后访问：

```bash
python -m http.server 8080
```

然后打开 `http://localhost:8080/prototype/`。

## 已实现的原型交互

- “目录设置”打开二级弹窗，可分别选择关单读取目录和截图保存目录。
- “关单清理”和“截图清理”分别打开对应的二次确认弹窗。
- “开始识别”会先检查关单读取目录；未设置时自动打开目录设置弹窗。
- “列表清理”使用独立确认流程，只清空当前列表，不删除源文件。
- “记录筛选”和“每页显示”是自定义可访问下拉组件，包含关闭、聚焦、展开、选中与键盘导航状态。
- 所有带图标按钮均以“24px 图标 + 10px 间距 + 文字”作为一个整体，在按钮容器内水平、垂直双向居中；不要使用文字基线对齐图标。
- 五个主操作分别使用独立业务图标，不复用通用垃圾桶或“扫描框＋播放键”等组合图形：关单清理显示关单文件删除，截图清理显示图片删除，开始识别显示扫描框内的关单文档，列表清理显示记录列表删除。
- 关单记录标题区与筛选区通过留白分组，不使用紧贴筛选标签的横向分隔线。
- 搜索框、统计数量、空状态和分页均保留了后续数据接线位置。
- 关单表格每条记录固定为 `56px`，超出可视区域后只在列表内部滚动。
- 空状态下只显示表头列分割线，表格内容区不显示贯穿到底部的竖线。
- “开始识别”和“列表清理”共享 44px 高度、7px 圆角、24px 图标和同一边框结构，只使用主色/危险色区分操作语义。

原型中的文件选择受浏览器安全限制，只能显示所选文件夹名称。制作 Windows 客户端时，应替换为原生目录选择器并保存真实路径。

## 组件与业务接口映射

| 组件 ID | 用户操作 | 建议业务接口 |
| --- | --- | --- |
| `open-directory-settings` | 打开目录设置 | `settings.getPaths()` |
| `choose-declaration-directory` | 选择关单读取目录 | `filesystem.pickDirectory("declaration")` |
| `choose-screenshot-directory` | 选择截图保存目录 | `filesystem.pickDirectory("screenshot")` |
| `save-directory-settings` | 保存两个目录 | `settings.savePaths(paths)` |
| `clear-declaration-directory` | 清理关单目录 | `filesystem.clearDirectory("declaration")` |
| `clear-screenshot-directory` | 清理截图目录 | `filesystem.clearDirectory("screenshot")` |
| `start-recognition` | 开始 OCR 与核验流程 | `recognition.start(settings.declarationPath)` |
| `clear-list` | 清空内存中的记录 | `records.clear()` |
| `record-search` | 按报关单编号搜索 | `records.query({ keyword })` |
| `record-filter-trigger` | 筛选记录 | `records.query({ status })` |
| `page-size-trigger` | 修改每页数量 | `records.query({ pageSize })` |

## 建议数据模型

```ts
type RecordStatus = "normal" | "duplicate" | "error";

interface AppSettings {
  declarationDirectory: string;
  screenshotDirectory: string;
}

interface CustomsRecord {
  id: string;
  declarationNumber: string;
  overseasConsignee: string;
  contractNumber: string;
  exitCategory: string;
  destinationCountry: string;
  currency: string;
  totalValue: number;
  status: RecordStatus;
  screenshotPath?: string;
}

interface CurrencyTotal {
  currency: string;
  beforeDeduplication: number;
  afterDeduplication: number;
}

interface BatchSummary {
  totalDeclarations: number;
  duplicateCount: number;
  totalsByCurrency: CurrencyTotal[];
}
```

## “开始识别”状态建议

```text
idle → validating → scanning → verifying → completed
                   ↘ error ↗
```

- `validating`：确认关单目录存在且可读，截图目录存在且可写。
- `scanning`：读取图片/PDF并执行 OCR；按钮禁用并显示进度。
- `verifying`：检测重复单号、在线核验并保存截图。
- `completed`：更新统计卡和列表，并给出完成提示。
- `error`：显示具体原因与重试入口，不丢失已完成记录。

## 删除与清理安全规则

1. 所有目录清理必须展示二次确认。
2. 清理前重新解析并验证目标路径，禁止空路径、磁盘根目录、用户主目录和应用工作目录。
3. 优先移动到回收站；若必须永久删除，应在确认文案中明确说明。
4. 清理按钮执行期间需要禁用，防止重复触发。
5. “列表清理”只清理界面状态，不删除关单源文件和截图。

## SVG 编辑说明

- 主界面 SVG 使用 `inkscape:label` 进行图层分组，同时提供稳定的 `id`、`data-component` 和 `data-action`。
- 所有文字仍为 `<text>`，没有转曲，方便修改内容和字号。
- 按钮图标基于统一 `24 × 24` 网格、`1.8px` 圆角描边独立绘制；每枚图标均为完整语义造型，没有嵌入位图，也不通过叠加通用图标临时拼接。
- 主 SVG 链接 `assets/app-icon.svg`；客户端实际运行建议按显示尺寸选择 `assets/icons/` 中最接近的 PNG，不要把单一大图任意缩放到所有尺寸。
- 如果最终交付必须避免字体差异，可复制一份 SVG 后再将文字转曲；请保留本包中的可编辑文字版本。

## 客户端实现建议

- **PySide6 / Qt**：可用 `QFileDialog.getExistingDirectory`、`QTableView`、`QSortFilterProxyModel` 和后台 `QThreadPool`。
- **.NET / WPF**：可用 `CommonOpenFileDialog`、`DataGrid`、MVVM 命令和 `IProgress<T>`。
- **Electron / Tauri**：可复用本原型的 DOM/CSS，目录选择与删除通过安全的主进程/原生命令完成。

无论使用哪种技术栈，都应将文件系统、OCR、在线核验、截图和 UI 状态拆成独立模块，避免在按钮事件中直接堆叠业务逻辑。
