# 参与贡献

感谢你愿意花时间改进 CtrlCPulse。这是一个刻意保持很小的工具：单个 exe、无第三方依赖、无网络请求。提交前请先确认你的改动符合这个前提。

## 开发环境

- Windows 10 / 11（x64）
- .NET Framework 4.8（系统自带）
- Visual Studio Build Tools 2022（含 .NET 桌面开发组件），或任意带 Roslyn `csc.exe` 的 VS 安装
- PowerShell 5.1 或 PowerShell 7

不需要安装 .NET SDK。`build.ps1` 会自动通过常见安装位置或 `vswhere` 找 `csc.exe`；如果你的环境比较特殊，可以用 `-Csc` 参数或 `CTRLCPULSE_CSC` 环境变量指定：

```powershell
.\build.ps1 -Csc 'D:\VS\MSBuild\Current\Bin\Roslyn\csc.exe'
```

## 构建与测试

```powershell
.\build.ps1                                                # 编译 dist\CtrlCPulse.exe
.\build.ps1 -Test                                          # 编译 + 25 项逻辑自检
pwsh -STA -NoProfile -File .\tools\verify-clipboard.ps1    # 13 项端到端剪贴板验收
.\tools\verify-visual.ps1                                  # 44 项像素级视觉验收
```

提交 PR 前至少跑通前两条；改动涉及渲染、布局、DPI 时请把第三条也跑一遍。三套验收的细节见 [docs/TESTING.md](docs/TESTING.md)。

注意：两套验收脚本会临时改写系统剪贴板（结束后恢复），并会结束同名的 `CtrlCPulse` 进程。

## 代码结构

| 路径 | 职责 |
| --- | --- |
| `src/Program.cs` | 入口、命令行参数、全局异常兜底 |
| `src/SingleInstance.cs` | 互斥量 + 激活事件 |
| `src/ClipboardWatcher.cs` | `WM_CLIPBOARDUPDATE` 监听与防抖 |
| `src/ClipboardInspector.cs` | 读取剪贴板、类型判定、预览文本与缩略图 |
| `src/Snapshot.cs` | 一次复制的内容快照 |
| `src/ToastForm.cs` | 分层窗口卡片：绘制、动画、鼠标交互 |
| `src/ToastQueue.cs` | 卡片堆叠、布局、超时驱逐 |
| `src/ToastContent.cs` | 各类提示的文案 |
| `src/Theme.cs` | DPI 缩放后的尺寸、颜色、字体 |
| `src/TrayContext.cs` | 托盘图标、菜单、状态切换 |
| `src/SettingsStore.cs` | 便携 INI 配置 |
| `src/Autostart.cs` | 开机自启注册表读写 |
| `src/NativeMethods.cs` | 全部 P/Invoke |
| `tests/TestMain.cs` | 自检入口 |

## 代码风格

- C# 7.3 语法即可，不要引入需要 NuGet 的新依赖；
- 4 空格缩进，文件 UTF-8（带 BOM 或不带都可以），注释用中文；
- 平台调用集中在 `NativeMethods.cs`，业务代码不直接 `DllImport`；
- 注释解释"为什么"，不复述"做了什么"；
- 新增可见行为时，同步更新 README 或 docs。

## 提交规范

提交信息用中文，格式：

```text
<type>: <一句话摘要>

- 具体改动 1
- 具体改动 2
```

`type` 取值：`feat` / `fix` / `perf` / `refactor` / `test` / `docs` / `chore`。

一次提交只做一件事；格式化、重命名等无关改动不要混进功能提交。

## 新增一种内容类型的步骤

1. `Snapshot.CopyKind` 增加枚举值，并在 `KindLabel` 给出中文标签；
2. `ClipboardInspector.ReadOnce` 增加判定分支（注意优先级顺序）；
3. `Theme` 增加强调色，`ToastForm.DrawContent` 增加对应的正文/缩略图绘制；
4. `ToastContent` 增加演示文案，`DemoRunner` 补一步；
5. 在 `tests/TestMain.cs` 增加快照构建与边界用例；
6. 同步 `tools/verify-visual.ps1` 的色条颜色断言。

## Pull Request

- 从 `main` 切分支，命名如 `feat/files-card`；
- 描述里写清楚"改了什么 / 为什么 / 怎么验证的"；
- 保持 diff 聚焦，避免顺手重构无关代码；
- 如果改变了用户可见行为，附一张 `dist\verify\` 下的截图。

## 发布流程（维护者）

1. 更新 `CHANGELOG.md`，把 `Unreleased` 的内容归入新版本；
2. 确认 `.\build.ps1 -Test` 与两套验收全绿；
3. 打标签并推送：`git tag v1.0.0; git push origin v1.0.0`；
4. `.github/workflows/release.yml` 会自动构建并创建 Release，附带 `CtrlCPulse-v1.0.0.zip`（exe + 安装脚本 + 文档）。
