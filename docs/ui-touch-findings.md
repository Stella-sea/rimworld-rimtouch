# UI 触控联动：证据、修复与验收

## 范围与用户复现

RimWorld 1.7.4995 rev309，Waylith 渲染器；1920×1210 framebuffer、1280×807 UI。
窗口模式、系统缩放与设备尚未记录，不由尺寸推定。用户确认：竖滚可能改滑块，
横拖滑块带动页面；可拖 mod 设置窗口与内容联动。完全抬手后从区外边缘新起手
仍联动。侧边滚动条正常，未发生实际断触。

用户另确认：许多开关会快速跳动，可能关→开→关，不仅诊断开关。
“双击代码、原生 Windows 鼠标提升未拦截或刷新率导致重复”是待查猜测，
不能写成已证实的底层抓取故障，也不能要求用户再次确认是否完全抬手。

## 已有真机证据（修复前）

- gesture 1 / frame 5209：横21.33、竖3.33，directionClaim=false、
  distanceClaim=true 仍 claimed；5210 MouseDrag→Used。gesture 8 / 5478：
  横18、竖1.33，scroll 106.9793→106.3124；9–13 有同类证据。
  这是错误接管后消费事件，不是单纯未消费拖动。未直接记录滑块数值。
- gesture 16 正常 Ended/reset/空触点；17 / 5988 begin(active=false)。
  5993 局部起点y28.67、当前y47.33，命中区顶部92，却 hit=globalCurrent
  claimed。证明新手势混坐标误命中，不是旧状态未清。
- gesture 16：同窗口 id61228868 的 scroll 0→44，窗口(160,60)→约(167,16.33)，
  持续 active，期间有 MouseDrag 消费。不能凭窗口显示 RimTalk 归因模组冲突。
- 4954：同一触点/鼠标1559,961，eventMouse≈831,95，旧helper≈901,118。
  旧 localInsideViewport 布尔值不可直接当实际区内外真相。
- 4954 / 6666 有同帧多次 captureStart/Stop，不是1024预算耗尽。

## 只读 IL 核验

合法本机依赖（1.7.4995 rev309，Waylith / `UnityStubs` / `Assembly-CSharp`）中：

1. `Input.GetMouseButtonDown(0)` / `GetMouseButtonUp(0)` 直接读取帧级布尔值
   `Mouse.current.leftButton.wasPressedThisFrame` / `wasReleasedThisFrame`，
   在同一帧的所有 GUI pass（`Layout`、`MouseDown`、`MouseUp`、`Repaint`）中保持为 `true`。
   Windows 触控点按经 `WM_TOUCH -> DefWindowProc` 鼠标提升时，会在抬手帧同帧送入按下和抬起。
   `Verse.Widgets.ButtonInvisibleDraggable(Rect, bool)` 不检查 `Event.current.type`，
   同一次调用内先因 `GetMouseButtonDown` 设 `activeControl` 再直落命中 `GetMouseButtonUp`
   清零并返回 `DraggableResult.Pressed`——一帧跑几轮 GUI pass 就返回几次点击。
   全程序集扫描证实 `ButtonInvisibleDraggable` 共驱动 6 条下游路径：
   `ToggleInvisibleDraggable`（普通复选框）、`ButtonImageDraggable`（`CheckboxMulti`
   三态框及部分 Gizmo）、`Widgets.Dropdown`、`MedicalCareUtility.MedicalCareSetter`、
   `ButtonTextWorker(draggable:true)` 与 `Dialog_EditIdeoStyleItems`。
2. `GUI.DragWindow(Rect)` 捕获窗口后，每轮调用都会算位置，只有 `MouseUp` 或
   鼠标不再按住才释放；没有“只有未消费的 `MouseDrag` 才更新”的限制。
   所以仅 `Event.Use` 不足以防止 `Layout`/`Repaint` 阶段的窗口移动。
3. 坐标后端 `ImGuiGuiBackend.ToGui` 为 $M^{-1}(p - \text{translate}) - \text{offset}$，
   而 `ScreenToGuiPoint` 写成了 $M^{-1}(p - \text{offset} - \text{translate})$，绝对 `offset`
   与 `GUI.matrix` 缩放不一致。修复通过当前事件的真实局部坐标锚定鼠标，以
   `ScreenToGUIPoint` 的差分转换触点相对位移，消去偏移。
4. `WorldRendererUtility.WorldRenderedNow` 在 1.7 中已改为公有属性
   `WorldRendererUtility.WorldRendered`；`MainTabWindow_Inspect`、`WorldInspectPane`、
   `MainTabWindow_Architect` 本身位于 `Find.WindowStack` 中，`GetWindowAt` 已精确覆盖。

## 本轮修复与架构收敛

- **控件防连跳下沉至 `Widgets.ButtonInvisibleDraggable`**：按 `(frameCount, window, screenRect)`
  对非 `Idle` 结果做同帧单次去重，并接入滚动/多指点击抑制。一次性覆盖普通复选框、
  三态复选框、下拉菜单等全部 6 条下游路径，避免先播音效再回滚，并保留原版跨行拖拽涂抹
  （`checkboxPainting`）与纯鼠标操作。注：`1.7.4995` 中 `Widgets.ButtonInvisibleDraggable`
  首参名为 `butRect`（见 `TestReports/26.11.24-1.md`），补丁参数须严格对齐 `butRect`；
  同时 `RimTouchBootstrap` 改为按补丁类逐个隔离应用，避免单一补丁签名异常阻断后续补丁挂载。
- **精确 HUD 与窗口命中（`UiHitTester`）**：移除吞掉平板 42%–56% 屏幕的 `190/90/170/700×380`
  魔法边距，改为基于 `WindowStack.GetWindowAt`、`MainButtonsRoot`（底 38px）、`ColonistBar.TryGetEntryAt`、
  `GizmoGridDrawer.HeightDrawnRecently`、右下全局控件及右侧信件/警报实际高度判断；
  双指缩放/扭转仅避让真实窗口（`IsBlockingWindowAt`），不再因边缘空白区失灵。
- **嵌套 `ScrollView` 与窗口互斥**：滚动只从同局部坐标、未外扩的 viewport 起手并保留侧边
  16 单位滚动条区域；增加同向滚动余量检查与嵌套内层 `ScrollView` 优先接管（内层触底自动交棒外层）；
  活动滚动/惯性绑定绘制窗口与局部矩形。
- **驱动状态机与反射精简**：合并 `Root.Update` Prefix（消除选降落点页与主菜单滞后 1 帧）；
  合并正常抬手与消失收尾为单轨 `CompleteActiveGesture`（补齐世界框选与双击记录）；移除旧版
  `TouchDropoutGraceFrames` 造成的双指松手 2 帧阻塞；提炼共享 `VelocityHistory` 消除 4 套重复环形缓冲；
  移除失效的 `WorldRenderedNow` 反射与 11 个公有成员反射装箱；转盘 `0.5x–2.0x` 灵敏度改为缩放目标位移。
- **有界诊断**：提供设置页临时勾选与“复制精简触控诊断”（无堆栈 JSON，1024 条上限），
  `scrollDecision` 直接映射核心真实分支枚举，不再二次重算。DLL 与 `Languages/` 须一并交付。

## 验证边界及剩余待办

隔离回归脚本（`test-input-proof.ps1`、`test-ui-touch.ps1`、`test-settings-layout.ps1`）
与 `Build17.ps1` 编译通过不代替真实渲染与触摸屏真机验证。
待真机验收：
- 设置页竖滚起于滑块不改值、横拖滑块页面不动，嵌套滚动列表内层优先与触底交棒；
- 普通开关、工作页三态框、下拉菜单单次点按不连跳，按住竖划批量涂抹复选框正常；
- 选中殖民者时左下及四周空白地面单指平移、长按右键、双指缩放正常，点按 Inspect/Gizmo/底栏仍走 UI；
- 地球 `0.5x / 1.0x / 2.0x` 转盘拖动速度及双指缩放平移无锁北抖动；
- 窗口释放/鼠标切换、关闭触控、失焦/恢复与返回主菜单清理。

## 用户验收反馈与设置页溢出修订

用户反馈本地测试包中已测项目“都完美”；但 RimTouch 设置页显示内容变少，
不再出现滚动，因此“竖滚起于滑块不改值”未测，不能记为通过。
本次反馈未逐项给出失焦/返回主菜单等清理测试结果，不扩大为全矩阵验收。

对照 main，本轮没有删除原有设置控件；新增诊断勾选和复制按钮。
只读核验 1.7.4995 rev309 的 `Listing.NewColumnIfNeeded`：默认在
`curY + height > listingRect.height` 时换列，`NewColumn` 将 curY 清零、
curX 加列宽和17，`CurHeight` 只返回当前列 curY。设置页的宽度只容一列，
却每轮用 CurHeight 回写 settingsHeight；内容超过初始1000单位时可能换到
被裁掉的右侧，并把最后一列高度当成总高，后续范围继续缩小。
这是源码/隔离回归证据，尚未通过用户截图或新日志逐帧确认实际布局。

设置页显式启用 `listing.maxOneColumn`，沿用原生列表和竖向滚动容器，
所有控件、设置键和默认值不变；下一轮用完整列表高度更新滚动范围。
`scripts/test-settings-layout.ps1` 编译真实设置页，覆盖连续五轮 GUI、
三种模拟文字行高、九个滑块、最后一个重置按钮及底部滚动可达；
`-VerifyRegression` 去掉单列设置，确认旧路径触发换列失败。
这是隔离布局测试，不代替真实渲染。后续仅需恢复页面后补测竖滚滑块，
此前已通过的交互反馈仍保留。

## 世界地图双轴模式：修复前只读调查（2026-11-24）

本节独立于前面的 UI 验收。用户报告：双轴模式单指拖动严重抖动，并改变北向；
本轮要求分析与修改指导，不实施修复。目标理解为单指更新两个轨道自由度并保持
已有北向倾角，双指扭转显式改变第三个滚转自由度，后续操作从当前姿态继续。
“圆形区域边缘顺滑”的具体命中容差尚未确认。

### 已核验的源码和游戏变换

- 本机 1.7.4995 rev309 的 `WorldCameraDriver.ApplyPositionToGameObject` IL：
  使用当前姿态（或原生动画插值姿态）的逆四元数设置摄像机 rotation，再以该
  摄像机 forward、altitude 及层偏移计算 position。不能把 `sphereRotation`
  当作世界中实际旋转的球体 Transform。此核验只读程序集，没有启动游戏。
- `TryGetWorldSpherePointAtGui` 从真实摄像机射线与世界球面求交，输出世界坐标。
  但 `UpdateWorldPanAnchorLocal` 将抓点乘 `Inverse(sphereRotation)` 存储，
  `ApplyProjectedTurntableDrag` 再乘当前 `sphereRotation` 重建所谓地理抓点。
  在上述摄像机约定下，固定地理点被再次随姿态移动，反馈误差重复计入视角变化。
- 该求解器的 `north = sphereRotation * Vector3.up` 属于视图表示，而
  `camera.transform.forward/right` 属于世界表示；两者直接叉乘混用坐标空间。
  主路径和 `ApplyTurntableDrag` 回退/惯性路径都有这套轴计算。
- 主路径将两个允许轴上的角速度投影合成为一次有限轴角旋转。
  “瞬时角速度没有被删除轴分量”不足以保证有限更新后北向倾角不变。
- 单指拖动开始时已关闭 `lockNorthUp` 并取消自动回北。历史上的原版锁定争抢
  不能直接当成本次问题的既定原因。双轴抓点缺失时仍走原版 dolly；
  拖动期间射线失交会切到固定系数转盘，抬手惯性也改用固定系数转盘。
  因此拖动、边缘回退和惯性的输入映射并不统一。

### 独立数值反例及证据边界

以下是按上述变换约定计算的简化数学反例，不是触摸屏实测或完整游戏帧回放：

- 初始姿态为单位四元数，中心抓点沿纯横向移动到相当于 5° 的固定目标后停住。
  主路径重建抓点的角度为 q，当前射线交点为 5°−q；反馈更新得到
  `q_next = q + (5° - 2q) = 5° - q`，于是逐帧为 5°、0°、5°、0°。
  使用固定地理抓点的参照则在第一次到达 5° 后保持不动。
  这证明当前算法自身可以产生静止触点下的往返纠正；现场是否叠加原生动画、
  其他补丁或输入噪声，尚无逐帧姿态日志确认。
- `Q = Rx(20°) Ry(40°)` 时北向投影初始竖直。用当前混合坐标轴施加纯 5°
  俯仰后北向偏移约 −3.994°；统一在视图空间计算轴的参照为 0°。
- 即使统一坐标，在北向前倾 30°、两个允许轴各贡献 10° 的例子中，合成轴角
  更新仍带来约 1.133° 北向偏移；把同样的角速度输入拆为 60 次并重算轴后
  约为 0.018°。这只是有限步长漂移的反例，不代表相同触点轨迹的完整回放。

### 修改建议与待验证

建议局部重构双轴姿态求解器，保留原生输入后端、通用触控生命周期和 UI 仲裁。
先固定世界/视图空间与四元数乘法约定；用当前视向和独立滚转参数表达姿态，
单指只更新两个轨道参数，双指扭转只更新滚转参数。可采用
`Q = RollView(roll) * NorthUp(viewDirection)` 的表示；复用已有北向姿态公式
作为姿态构造的一部分，一次提交姿态，避免抓点纠正与回正各写一次相互争抢。
具体输入方向、灵敏度和极点策略须有反例验证，不能直接把公式当成已验收实现。

双轴拖动、双指中点平移和惯性应共用同一求解器；圆边缘用于起手资格/连续速度
映射，已接管手势不要因逐帧球面失交切换旋转模型。双指扭转后的重新抓点必须
同步手指基准，避免新抓点又追逐旧起点累计位移。双轴路径中的自由三轴重新锚定
也需纳入统一求解，明确视图中心滚转与指间中点锚定的取舍。

待验证：静止触点后的零运动；纯横/纯纵/斜向及闭环拖动的北向不变；
先扭转再单指拖动保持新倾角；不同采样率、圆边缘、惯性交接及极点连续性。
现场模组 DLL 身份、窗口/UI/系统缩放和设备仍待对应本次复现记录。

## 世界地图双轴重构：实现与验收边界

用户已授权修改，并选择两个 A：滚转后将屏幕位移投影到当前地平轴，使画面
跟随手指方向；球外非 UI 空白区也能起手转球。额外要求保留球外点按取消选择。

实现：

- 删除投影抓点反馈及假“球体局部锚点”。双轴单指、双指中点平移、惯性共用
  `ApplyDualAxisTurntableDrag`，每次读取当前 `sphereRotation`。
- 在相机到世界空间中依次俯仰、绕世界北轴偏航，并用当前滚转重新构造姿态。
  滚转提取、姿态构造与归一化使用 double 中间量，避免长时单精度累积漂移；
  不缓存加载时或拖动开始时的姿态。位移先换成 framebuffer 像素，再按当前
  FOV、viewport 高度和球面中心距离标定；已有灵敏度及反向设置继续生效。
- 新操作将纬度限于 ±85°。外部跳转若已超出范围，水平拖动不会突然拉回；
  允许向内逐步返回。精确极点的北向本身退化，不能许诺该点有唯一北向倾角。
- 双轴扭转仅改变视图滚转，不再叠加自由三轴重新锚定。自由模式保留抓点，并修复
  世界纠正四元数的右乘顺序。直接姿态更新统一停止原生跳转/回正插值。
- 仅在双轴拖动/双指手势开始时关闭原版锁北，不在普通逐帧更新中反复关闭；
  原版北向按钮产生的新姿态会作为后续更新基准。
- 点击分类沿用 18 GUI 单位阈值。球外点按及小幅抖动仍只取消选择；
  已进入拖动后即使返回起点抬手也不触发点击。释放逻辑额外检查
  `suppressReleaseTap`；UI 起手、双击拖框选沿用原有仲裁。

验证使用 `scripts/test-world-touch.ps1`：编译真实 `WorldMapTouchCamera`、
`TouchInertia`、`VelocityHistory`、`TwistGesture`，以及从真实驱动提取的
单指世界处理、双指处理和释放方法。测试替身是静止地理球面、逆姿态摄像机和
独立 System.Numerics 单精度旋转；通过静止地物的实际投影验证方向与抓点误差。

64 个经纬度/滚转组合分别画十圈（3600 步），最大北向变化约 0.000792°；
纯横/纵/斜向方向、停住后的零更新、双指扭转后保持倾角、UI 缩放、灵敏度、
反向、极点限幅、近极点内拉不跳滚转、双指扭转松手不误触发平移惯性、采样率、
球内外同位移、球外点按/拖回起点/释放抑制、球内选点、UI 点按、双击拖框、
真实惯性及双指分派、原版回北接续均通过。
自由抓点最大投影误差约 0.000205 GUI 像素。三个可编译负对照分别重引入
抓点左乘、世界/视图轴混用、球外点按误判拖动，都被行为断言拒绝；已接入 CI。

后续审查收口（扭转松手帧抑制平移速度采样、近精确极点 `Atan2` 退化保护、
`ApplyWorldTwist` 提交后再清锁北、`Screen` 零尺寸及 `RimTouchMod.Settings`
判空、行尾按 `.gitattributes` 统一）已合入源码并通过全套免游戏依赖 CI 回归；
本机 `Build17.ps1` 重新编译及 `scripts/test-harmony-patches.ps1` 需在具备
RimWorld 1.7 与 Harmony 依赖后补跑（此前预收口版本的本地 DLL SHA256 不再对应
当前源码）。历史构建与源快照记录保持未改动。

这些是源码隔离回归，不能宣称浮点误差严格为零或真机无抖动。
本轮真机待验收：球外点按清空选择、球外拖动不清空、跨圆边连续、扭转后随手
方向与北向保持、极点与惯性、原版回北按钮、鼠标切换和失焦/主菜单清理。
测试时补记窗口模式、UI/系统缩放、设备及实际 DLL 身份。

## 滚动与滑块共存时的间断掉点与跃变：实证、IL 核验与修复（2026-11-24）

### 用户复现与真机精简诊断证据

用户在 RimTouch 设置页（`RimWorld.Dialog_ModSettings`，`1920×1210` framebuffer，
`outRect=(0,40,864,584)`、`viewRect=(0,0,844,1004)`、`scrollPosition.y=0`）复现：
纯横向拖滑块正常，手指略离原高度后出现间断掉点与跃变；无滚动条页面无论拉多远
都顺畅。精简诊断记录 `gesture=1`（`finger=20685`，`F2996`–`F3554`）：
- 全程 `cnt=1, cancel=False`，排除硬件断触与窗口取消；
- `F2996`–`F3136` 纯横向 `|drag.y| <= 5.3`，除跨起点 ±6 GUI 单位外均放行；
- `F3141`–`F3321` 手指自然下偏 `25~77` GUI 单位后，旧 `FilterHorizontalSliderDrag`
  每帧重算累计位移 `|dx| <= 6 || |dx| <= 1.5|dy|` 且不锁定状态，在 `F3213 (12,51.4)`、
  `F3249 (12,67.4)`、`F3267 (48.7,76.7)`、`F3303 (2.7,70)`、`F3321 (29.3,56)` 及
  `F3502 (4.7,9.3)` 反复消费 `MouseDrag`，跳出扇形死区后又瞬间放行造成跃变；
  且上述多帧 `directionClaim=True`，若页面已下滚过（存在向上余量），滚动会在中途
  直接抢走滑块手势。

### 只读 IL 核验（RimWorld 1.7.4995 rev309）

检查 `Assembly-CSharp.dll` 与 `UnityStubs.dll`，全游戏 12 个滑块/范围滑块入口
全部汇聚到 5 个底层方法，但底层行为分为 3 类：
1. `Verse.Widgets.HorizontalSlider`（9 参，承载 `Listing_Standard.Slider` /
   `SliderLabeled`、`Widgets.HorizontalSlider`(5 参) 与 `FrequencyHorizontalSlider`）：
   `MouseDown` 仅写 `Widgets.sliderDraggingID` 并 `Use()`，不改值；拖拽更新依赖
   `sliderDraggingID == id && UnityGUIBugsFixer.MouseDrag(0)`（Windows 下要求
   `EventType.MouseDrag`），消费 `MouseDrag` 不会清除 `sliderDraggingID`。
2. `Verse.Widgets.FloatRange`（11 参）与 `Verse.Widgets.IntRange`（7 参，承载
   `FloatRangeWithTypeIn`、`Listing_Standard.IntRange`）：使用 `Widgets.draggingId`
   与 `Widgets.curDragEnd`，**`MouseDown` 当场改值**，拖拽期同样检查 `MouseDrag(0)`。
3. `UnityEngine.GUI.HorizontalSlider` / `VerticalSlider`（4 参，Waylith 托管实现，
   承载 `LudeonTK.DevGUI.HorizontalSlider` 与 `GUI.Slider`）：使用 `GUI._guiSliderDragId`，
   **`MouseDown` 及后续每一轮 `Layout`/`Repaint` 只要 `Input.GetMouseButton(0)` 为真
   就会直接改值**，单靠 `Event.Use()` 拦不住。

### 本轮修复

- **一次性方向仲裁 + 全手势锁定（`TouchInputDriver.UiScroll.cs`）**：
  - 引入 `UiSliderArbitrationState`（`None` / `Pending` / `Committed`）与 `ScrollDecision.ControlLocked`，
    在 `UpdateOneFinger` 新手势起手与 `ResetState` 清零。
  - 在可滚动候选窗口内保留 6 GUI 单位起手死区：`|dx| > 6 && |dx| > 1.5|dy|` 立即锁定
    `Committed`；`|dy| > 6 && |dy| > 1.5|dx|` 且有余量由滚动接管；斜向超过 12 GUI 单位
    按倾向决断（横向滑块 `|dx| >= |dy|` 锁滑块，`|dy| > |dx|` 有余量归滚动、无余量不误锁滑块）。
  - 一旦 `Committed`，滑块侧全部事件无条件放行，滚动侧返回 `ControlLocked` 永不中途抢占，
    同时继续由 `ShouldAllowWindowDrag` 阻止窗口误拖；一旦滚动接管，`ClaimTouchForUiScroll`
    通过 `ReflectionGuard` 调用 `Widgets.ResetSliderDraggingIDs()` 并清零 `GUI._guiSliderDragId`。
  - 双向可滚容器中命中横向/纵向滑块处于 `Pending` 时，屏蔽同轴滚动资格防止抢占；
    同窗口中未命中的其他滑块绝不替当前滑块消费 `MouseDrag`。
- **全滑块补丁与快照护值（`RimTouchBootstrap.cs`）**：
  - 升级 `WidgetsHorizontalSliderTouchPatch`（Prefix/Postfix 绑定 `sliderDraggingID`）；
  - 新增 `WidgetsFloatRangeTouchPatch`、`WidgetsIntRangeTouchPatch`、`GuiHorizontalSliderTouchPatch`、
    `GuiVerticalSliderTouchPatch`（`GUI.*Slider` 均带 `Prepare()` 托管方法体检查），
    在 `Pending`/已滚动状态下通过 Prefix/Postfix 值快照防止 `MouseDown` 与 `Layout`/`Repaint` 误改值；
    无滚动条页面完全直通原版。
- **诊断与隔离回归**：
  - `TouchInputDriver.Diagnostics.cs` 补齐 `ScrollDecision.ControlLocked -> "controlLocked"`
    及状态转移级 `sliderArbitration` 事件；
  - `scripts/test-ui-touch.ps1` 补齐真机日志漂移轨迹与负对照断言、斜向 12px 决断、
    多范围滑块防误吃、`FloatRange`/`IntRange`/`GUI.HorizontalSlider` 快照护值与双向滚动防抢占。

### 待真机验收

- RimTouch 设置页（页面顶部及下滚后各位置）：滑块纯横拖、拖出轨道上下 70+px 折返跨起点均顺滑无掉点且页面不抢滚；从滑块起手竖滚页面不改滑块值；
- 含 `FloatRange`/`IntRange` 的列表页（如服饰/食物/清单策略）：竖滚不误改范围，横拖端点顺滑；
- 调试窗（`DevGUI.HorizontalSlider`）及无滚动条页面滑块行为正常。
