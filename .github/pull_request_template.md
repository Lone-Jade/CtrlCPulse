## 改了什么

<!-- 一句话概括，再列要点 -->

## 为什么

<!-- 解决的问题或动机；关联的 issue 用 "Closes #123" -->

## 怎么验证的

- [ ] `.\build.ps1 -Test`（25 项逻辑自检）
- [ ] `pwsh -STA -NoProfile -File .\tools\verify-clipboard.ps1`（13 项端到端验收）
- [ ] `.\tools\verify-visual.ps1`（44 项视觉验收，改动涉及渲染/布局/DPI 时必跑）
- [ ] 手工检查：<!-- 例如在记事本、资源管理器、截图工具里各复制一次 -->

## 截图

<!-- 涉及界面变化时附 dist\verify 下的截图，或直接拖拽图片 -->
