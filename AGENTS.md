# 项目工作指南

## 项目归属与范围

- 本目录维护 GitHub 仓库：<https://github.com/Stella-sea/rimworld-rimtouch>。
- RimTouch 是 Tatsuki Tanaka 原版 RimTouch 的 Windows 触控续作，当前目标为 **RimWorld 1.7**，元数据仍标为 Candidate。
- 上游：<https://github.com/iLtanaka/RimTouch>。保留原作者、续作署名和 GPL-3.0-or-later 许可；预览图另受 `ASSETS.md` 约束。
- 先阅读 `README.md`、`BUILD-STATUS.md`、`Build.json` 和相关源文件。只改当前任务需要的内容，优先复用现有输入、手势和安全回退机制。
- 默认使用中文与用户沟通；代码标识符、现有英文界面文案和日志沿用所在文件的风格。

## 目录与架构

- `Source/RimTouchBootstrap.cs`：Harmony 初始化及游戏/UI/摄像机补丁入口。
- `Source/RimTouchMod.cs`：模组设置、设置持久化、扩展缩放范围。
- `Source/TouchInputDriver.cs`：触控状态机、输入仲裁、选择与手势协调。
- `Source/LocalMapTouchCamera.cs`、`WorldMapTouchCamera.cs`、`TouchInertia.cs`、`UiHitTester.cs`、`TouchInputDriver.UiScroll.cs`：同一个 `TouchInputDriver` 的 partial 实现，分别处理地图摄像机、世界摄像机、惯性、UI 命中和滚动。
- `Source/TouchTapRepair.cs`、`TouchMode.cs`：UI 触点修复和手势模式。
- `Source/ReflectionGuard.cs`：受保护的游戏内部成员访问及去重警告。
- `Source/Input/`：`RimTouch.TouchPlatform` 命名空间。包含 Windows 原生触控后端、触点帧缓冲、Unity 输入适配、扭转手势、缩放所有权和可选诊断。
- `About/`：游戏识别的模组元数据、Workshop ID 和预览图。
- `Source-Snapshot.json`：已发布源快照记录；`BUILD-STATUS.md`：历史构建证据。不要把历史记录当作当前改动的验证结果。
- `.build/1.7/`：本机构建输出，已被 Git 忽略。

## 构建

支持的入口是根目录 `Build17.ps1`，需要 PowerShell 7、已安装的 .NET SDK、合法的 RimWorld 1.7 安装和对应的 Harmony DLL：

```powershell
pwsh -File ./Build17.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'
```

- `GameDir` 应直接包含 `Version.txt`、`Assembly-CSharp.dll` 及所需游戏 DLL；脚本要求版本号以 `1.7.` 开头。
- 脚本选择已安装 SDK 中的 Roslyn，递归编译 `Source/` 下的 C# 文件，排除 `bin/` 和 `obj/`；程序集名称由 `Build.json` 决定。
- 输出为 `.build/1.7/RimTouch.dll` 和 `.build/1.7/Build-Receipt.json`。收据记录依赖与源文件哈希、诊断和构建结果。
- 构建前至少需要 1.5 GB 可用空间。构建脚本不下载依赖、不启动游戏，也不改动已安装模组。
- `Source/RimTouch.csproj` 是精简的旧式项目文件，不是本快照支持的构建入口；不要默认使用 `dotnet build` 或 `dotnet test`。
- 游戏或 Harmony 路径未知时向用户确认，不猜测安装位置，也不提交个人机器路径或专有程序集。

## 修改约定与安全边界

- 沿用现有 C# 风格：四空格缩进、独占一行的大括号、类型/方法 PascalCase、字段/局部变量 camelCase。避免无关重排和全文件格式化。
- 优先在现有 partial 文件中修改对应职责；游戏适配和平台输入保持分层。
- 触控失效、关闭或不可用时，保留鼠标操作路径。调整原版输入抑制时核对 UI、地图工具、窗口关闭和触控释放阶段，防止重复点击或残留抑制状态。
- Windows 原生回调不得向非托管游戏循环传播托管异常。保留回调委托的生命周期、窗口线程/进程校验、消息转发及清理失败后的安全保留逻辑。
- 保持焦点丢失、窗口几何变化、触控取消和返回主菜单时的清理行为，避免残留触点或惯性。
- 明确区分 Windows 客户区归一化坐标、底部为原点的 framebuffer 像素和 GUI/UI 缩放坐标。
- UI 命中统一到当前局部坐标，只用同坐标触摸起点决定滚动资格；禁止以全局触点或停留鼠标与局部矩形取 OR 兜底。Waylith 的 GUI 绝对偏移与缩放行为须通过事件锚点/差分验证，不能仅凭函数名推定单位。触控滚动及惯性绑定绘制窗口，边缘新起手仍走原版窗口拖动。
- 游戏内部成员访问优先沿用 `ReflectionGuard` 的检查、失败回退和去重日志模式。修改 Harmony 目标或 IL 匹配时保留版本不匹配时的安全退路。
- 世界地图按“静止球体、移动相机”推导：`sphereRotation` 是世界到视图旋转，相机姿态为其逆；世界抓点纠正右乘，视图扭转左乘，禁止混用世界/视图向量。双轴单指、中点平移及惯性共用增量求解器，保持当前滚转并按屏幕方向映射；球外空白可拖动，但阈值内点按仍取消选择，拖回起点抬手不得误点。数学回归必须投影静止世界地物，不能用转球模型替代。
- 扩展缩放通过 `ZoomRangeLease` 管理所有权，避免恢复时覆盖其他模组的修改。额外 UI 缩放是显式启用选项，保留原生安全选择和重置入口。
- 设置键及默认值涉及已有用户配置；变更时检查 `RimTouchSettings.ExposeData()` 和设置版本迁移。不要在每次 GUI 绘制时写入设置。
- 诊断保持显式启用且有界。用户要求不依赖启动参数增加测试负担：优先设置页临时勾选、直接复制原生 Debug log，不持久化开关、不要求重启或另找文件。每轮最多 1024 条，停止/耗尽后可重新勾选；旧启动参数仅保留兼容。不要默认开启或提交玩家日志。记录滚动命中时须保留处理前的事件类型与鼠标位置，因为核心可能消费 MouseDrag 将事件改为 Used。
- 诊断提供“复制精简触控诊断”，保留原生日志，复制内容不带逐条调用栈；同容器/决策跨 GUI pass 去重。分析所得证据与假设分开写入 `docs/ui-touch-findings.md`，待验收或未修复问题进入项目待办，避免依赖长会话和整段玩家日志。翻译交付同时包含 DLL 和 `Languages/`，仅替换 DLL 不会更新文本。
- 未获明确授权时，仅在仓库内修改和构建；安装 DLL、改动启用模组列表、启动游戏、改动玩家存档、发布 Workshop 或推送 GitHub 均需单独授权。
- 保留 `About/About.xml` 的包 ID、依赖、署名和 `About/PublishedFileId.txt`，除非任务明确要求变更发布身份。GitHub 源码更新不等于 Workshop 更新。
- 不提交游戏/Harmony/provider DLL、构建产物、存档、密钥或个人配置。遵守 `.gitignore`，不要为提交依赖而绕过忽略规则。

## 验证与交付

- 当前仓库没有自动化测试工程或 CI 配置。每次交付说明实际执行过的检查及未验证部分。
- 若依赖齐备，使用上述脚本编译并检查诊断和构建收据。缺少依赖时明确说明无法编译，不以历史成功结果替代。
- 编译只验证对应游戏 API 的可编译性，不证明触摸屏、渲染器、显示缩放、其他游戏版本或任意模组组合兼容。
- 经用户授权开展真机测试时，按改动范围检查：主菜单和游戏内触控、点击/双击/长按选择、拖动和双指缩放、UI 滚动及误触抑制、世界地图平移/缩放/扭转、鼠标切换、失焦/恢复、关闭触控和返回主菜单后的清理。
- 手势或坐标改动应记录测试使用的游戏版本、窗口模式、UI/系统缩放和触控设备；无法测试的项目明确标为未测试。
- 修改源码后不要宣称与原始快照仍逐字节一致。仅在任务涉及发布记录且有新证据时更新 `BUILD-STATUS.md` 或 `Source-Snapshot.json`。
- 提交前检查 `git diff --check`、差异范围和 `git status --short`。交付概述改动文件、行为影响、验证结果及已知限制；未经请求不自动提交或推送。

## Git 分支、CI 与发布规范

- **分支职责**：`main` 为受保护的稳定主干（仅接收实机验收通过的变更）；`dev` 为日常集成与预发布分支；具体功能/修复从 `dev` 切出短生命周期分支（`feat/*`、`fix/*`、`refactor/*`、`docs/*`、`ci/*`）。
- **PR 与合并策略**：短分支合入 `dev` 采用 **Squash and merge** 保持线性整洁；`dev` 验收后同步至 `main` **必须采用 Create a merge commit**（严禁 Squash），保证 `dev` 与 `main` 共享提交历史与标签祖先关系。
- **双层检查流水线**：
  - 每次提交/PR 须确保免游戏依赖检查全部通过：`scripts/check-repo-hygiene.ps1`、`scripts/check-csharp-syntax.ps1`、`scripts/test-input-proof.ps1`、`scripts/test-ui-touch.ps1`、`scripts/test-settings-layout.ps1`、`scripts/test-world-touch.ps1`（后者同时执行 `-VerifyRegression`，与 `.github/workflows/ci.yml` 一致）。
  - 本机具备 RimWorld 1.7 与 Harmony 依赖时，依次执行 `Build17.ps1`、`scripts/test-harmony-patches.ps1`（核验全部 `[HarmonyPatch]` 目标与参数名绑定）及 `scripts/package-release.ps1`。
- **标签与发版**：预发布标签格式为附注标签 `v0.1.0-test.<N>`（与 `About/About.xml` `<modVersion>` 及 `CHANGELOG.md` 严格一致，由 `release-tag-check.yml` 校验并受 GitHub Ruleset 不可变保护）；正式版 `v0.1.0` 打在 `main` 上且须先移除中文 `About.xml` 的“测试版”标记。玩家日志（如 `TestReports/`）保持 Git 忽略，严禁入库。

