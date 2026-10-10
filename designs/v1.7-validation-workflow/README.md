# 关单核验台 v1.7：已确认的校验流程与视觉稿

本目录保存 v1.7 已确认的设计基线。设计沿用 `docs/design-handoff/customs-console-vector-handoff` 的颜色、字号、间距和控件尺度，对应功能已经在 v1.7 源码中实现。

## 评审范围

- 自绘单项右键复制菜单，替换 Windows 原生菜单，只保留“复制选中的内容 / Ctrl+C”。
- 五枚主操作图标采用圆角线性扁平风格，并改为逐尺寸导出的透明 PNG，避免运行时浮点描边造成上下线条不一致。
- 关单概览以 17px 标题为主层级，金额采用 15px 常规字重作为从属信息；数量指标保持 30px。
- 将在线核验从“回到软件确认后截图”改为“自动检测查询结果并截图”。
- 明确网页结构变化、超时、浏览器连接中断时的降级入口。
- 记录 `7063(1).pdf` 中 `HNB_TX_US` 被错误拆分的根因与修正规则。

## 预览

通过静态服务器打开 `index.html`。页面顶部四个标签可切换：

1. 右键复制菜单
2. 精简校验流程
3. PNG 图标规范
4. 下划线识别修正

也可使用查询参数直接定位：`?view=menu`、`?view=flow`、`?view=icons`、`?view=ocr`。

## 目录

- `index.html`：交互原型结构
- `styles.css`：高保真视觉样式
- `app.js`：标签切换、右键菜单和流程状态演示
- `assets/button-icons/`：24/32/48px PNG 候选图标
- `assets/icon-source/generate_icons.py`：PNG 图标生成母版
- `screenshots/`：浏览器验证后的效果图
- `DESIGN_DECISIONS.md`：流程、边界与后续实现约束
