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
   `MainTabWindow_Architect` 本身位于 `Find.WindowStack` 中，`GetWindowAt` 覆盖其
   `windowRect` 本体；它不代表窗口外附加控件均已覆盖，见后面的“地形标签穿透”调查。

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

## 世界地图“地形”标签穿透：只读根因调查（2026-11-24）

### 用户现象与范围

用户确认：世界地图选中地块后，点击左下信息面板上沿的“地形”标签，选点会变成
按钮后方的世界地块；预期只操作标签并保留原选点。用户要求一并检查共用机制的
隐含问题，优先统一输入归属，不为“地形”单独增加特例。本轮不修改运行源码。
本次现场设备、缩放、窗口模式及已加载 DLL 身份尚未采集，不沿用其他问题的记录。

### 已确认的源代码与游戏 IL 证据

只读核验本机 `1.7.4995 rev309` 的 `Assembly-CSharp.dll`：

- `WindowStack.GetWindowAt(Vector2)` 从上到下只检查各窗口的 `windowRect.Contains`，
  不查询窗口 `ExtraOnGUI` 绘制出的附加交互区域。
- `WorldInspectPane.SetInitialSizeAndPosition` 把窗口 y 设置为 `PaneTopY`；
  `InspectPaneUtility.PaneSizeFor` 返回面板宽度和 165 高度。
- `InspectPaneUtility.DoTabs` 把 `tabsTopY` 设为 `PaneTopY - 30`；
  其共享局部方法 `<DoTabs>g__Do|18_0` 为可见、非 Hidden 标签绘制
  `72 × 30` 的 `Widgets.ButtonText`。因此标签处于面板矩形上方。
  本地 `MainTabWindow_Inspect` 与 `WorldInspectPane` 都从 `ExtraOnGUI`
  调用同一个 `InspectPaneUtility.ExtraOnGUI -> DoTabs`，缺口覆盖两类面板的标签，
  不取决于标签文字或 `WITab_Terrain` 的具体实现。
- `InspectTabBase.DoTabGUI` 为打开的详情页登记独立 `ImmediateWindow`；
  `WITab_Caravan_Feeding/Health/Needs/Social.ExtraOnGUI` 的侧页也登记独立窗口。
  稳定登记时这些正文区域能由 `GetWindowAt` 命中，不能把它们全部记为漏判。
  `ImmediateWindow` 的登记/更新/移除只发生在 Repaint；动态切页的帧间边界仍待验证。
- `UIRoot_Play.UIRootOnGUI` 与 `UIRoot_Entry.UIRootOnGUI` 都先执行窗口栈 GUI，
  再调用世界 `HandleLowPriorityInput`；原生设计允许 UI 先处理事件。
  `UIRoot.UIRootOnGUI -> WindowStack.HandleEventsHighPriority` 还会通过
  `GetsInput(null)` 阻止吸收窗口之外的鼠标输入。

对照当前 RimTouch：

- `UiHitTester.IsLikelyUiPosition` 使用 `GetWindowAt` 加若干 HUD 范围，
  没有信息标签栏的实际交互矩形，也没有吸收窗口的全局输入规则。
  `IsBlockingWindowAt` 同样只有窗口本体和场景检查。
- `RootUpdatePatch.Prefix -> TouchInputDriver.Update` 在触点起手时保存
  `startedOverUi = IsLikelyUiPosition(gui)`。漏判后，普通短点按释放进入
  `CompleteActiveGesture -> DoWorldLeftClick`，直接射线取地块并调用
  `WorldSelector.SelectFirstOrNextAt`。此路径不检查 GUI 事件是否已消费。
  这是根因的第二部分：触控自行提交选点，绕开原生 UI 优先的派发次序。
  即使后续按钮正确调用 `Event.Use()`，也无法撤销已提交的触控选择。
- `WorldSelectorHandleWorldClicksPatch` / `SelectorHandleMapClicksPatch` 仅拦截
  原版选择入口，不拦截上述直接调用，不能补救自定义路径中的 UI 漏判。

### 隔离验证与同类问题

本轮执行原有 `scripts/test-ui-touch.ps1`，通过；然后复用同进程测试替身，
以反射调用实际 `Source/UiHitTester.cs` 编译出的两个私有命中方法，未改测试文件。
采用说明用的 UI 高度 807、面板 `(0,607,432,165)`、Gizmo 高度 0：

- 标签中心 `(396,592)`：本地/世界模式下均 `ui=false, blocking=false`；
- 面板正文 `(396,630)`：两种模式均 `ui=true, blocking=true`；
- 标签上方空白 `(396,550)`：两种模式均为 false，说明不能无界扩大面板范围。

这些是源码隔离验证，坐标不是本次用户现场测量。原有测试只验证面板正文，
没有标签栏用例，因此现有测试通过不能排除该缺口。

已证实的共同覆盖缺口：

- 本地和世界信息标签的起手漏判；同一缺口也进入双指窗口阻挡判断。
- 模态/吸收窗口在自身矩形之外的输入阻挡规则未进入触控地图资格判断。
  这是代码与原生语义的差异，现场是否出现背景选点或平移尚未实测。

待验证，不能记为用户已遇到的故障：

- Repaint 更新的 ImmediateWindow 在切页、关闭、尺寸变化时的登记时序；
- 非窗口 HUD，如资源分类展开、动态高度的世界全局控件，与当前固定范围的差异；
- 世界目标工具与触控选点的归属协调，及 Windows 提升鼠标事件的释放尾帧。

### 修正方向与验收要求（尚未实施）

最小的共用修正可覆盖两个 Inspect 面板的实际可见标签，而不按“地形”文本分支。
用户要求的底层收敛还需统一 UI 命中与手势归属：复用原生窗口层级和吸收规则，
从共用 UI 绘制/控件入口取得真实交互范围，覆盖窗口外标签和非窗口 HUD；
保持局部/全局坐标、有效帧、所属窗口与视图的一致性。

地图点按先保留为待提交意图，在 UI 完成派发、明确未被 UI/工具占用后提交一次。
同一手势的 UI、地图、工具归属需要锁定，并由原版输入抑制、自定义选点、
平移/长按/双指以及释放尾帧共同使用；仅延后调用或检查 `Event.Use()` 不足以
解决拖动起手漏判。拖动期间不应因手指后来划过 UI 就重新夺取已有地图手势。
布局缓存的失效与首次绘制需验证，不能用上一帧矩形宣称任意模组完全适配。

后续验证应覆盖本地/世界全部可见标签、详情正文和侧页、标签边界空白、
模态窗口外部、动态切页/关闭、单指长按和拖动、双指、快速连续点按、
不同 UI 缩放以及纯鼠标。未启动游戏、安装或构建新 DLL，也未修改运行源码；
根因有源码、IL 和隔离命中证据，尚无本次现场逐帧事件记录。

## 信息标签与手势归属：授权后的共用修复

用户接受收敛版 1＋2，并授权实施；第 3 点跨帧点按队列不在本轮实施。
工作分支为 `fix/ui-touch-ownership`。

### 实现范围

- `IsBlockingWindowAt` 共用 `WindowStack.GetsInput(null)`、窗口本体和 Inspect
  可见标签检查；`IsLikelyUiPosition` 复用该判断。标签按 1.7 原生的 72×30 布局、
  `PaneWidthFor`、`PaneTopY` 以及 `IsVisible/Hidden` 实时计算，覆盖本地/世界面板及
  多于六个标签的布局；不扩大整条空白区域，不记录全局控件矩形缓存。
- 新增 `TouchOwner` 区分 UI、本地/世界地图及两类工具。`startedOverUi` 从归属派生，
  单指更新、释放、原版选择抑制、窗口关闭资格不再按当前手指位置重算归属。
  普通抬手保留归属供已有释放保护使用，取消时清空，下一次起手重新确定。
  世界 UI 手势不再套用地图手势的 45 帧选择保护。
- 单指起手清除遗留原版拖框；UI 触控期间抑制原版地图选择。升为双指时保留
  窗口/滚动起手的阻挡，只对新增触点查询当前窗口命中；已有地图触点划过窗口
  不会因此重新归属 UI，新第二指落在窗口上仍阻止地图操作。普通非窗口 HUD
  保留原有双指入口。
- UI 滚动接管同步更新同一个归属。原有单指/双指模式、滚动方向仲裁、惯性、
  工具双指取消和坐标转换机制继续使用。
- `WorldTargeter` 独立识别并通过原生处理器接收所属鼠标事件；普通地图触控不再
  同时交给新激活的工具。新增补丁保留世界工具键盘取消。只读 IL 确认 `TilePicker`
  本身绘制确认按钮并读取已选地块，不直接处理地图鼠标选点，因此不把
  `TilePicker.Active` 当作整张世界地图交给工具的依据。
- 生命周期诊断加入 `owner` 字段，沿用现有临时有界诊断入口。
- `.gitignore` 增加 `*.lnk`，排除本机游戏目录快捷方式；未删除快捷方式。
  共用规则写入 `AGENTS.md`。

### 本轮实际验证

- 免游戏依赖的卫生、C# 语法、诊断、UI、设置布局和世界数学/交互检查通过；
  UI、设置和世界的负对照通过。UI 负对照去掉标签命中后，被标签中心行为断言拒绝，
  已接入 CI。
- UI 回归编译实际命中、单指处理、释放、输入抑制属性与世界工具补丁：
  覆盖标签点按/长按/拖动、多标签与隐藏标签、窗口关闭/取消选择/尺寸变化、
  模态窗口外部、本地/世界手势跨过 UI、Entry 界面世界工具、键盘取消、
  关闭触控和无触控鼠标放行。世界回归编译实际双指方法，补测窗口/滚动归属继承、
  已有地图触点划过窗口、新第二指落在窗口以及普通 HUD 双指入口。
- `Build17.ps1` 对 `1.7.4995 rev309` 编译通过，0 诊断；
  `test-harmony-patches.ps1` 验证 37/37 补丁目标及参数绑定；
  `package-release.ps1` 验证当前源码、DLL 和构建收据一致并完成本地打包检查。
- 本轮 DLL SHA256：
  `0ACF259FE05F7BDEC61BAB2682E1BE81275A1DFCFE948DC98269D0F6AC360522`。
  完整本地测试目录：`.build/ui-touch-ownership/local/RimTouch`，包含 DLL、
  About、Languages 和许可说明。沿用 test.15 元数据，不代表新标签或已发布版本。

### 真机待验收与边界

尚未安装、启动游戏或进行触摸屏验证；未提交、推送或发布。
优先复测原始“地形”点按保留选点，再检查本地其他信息标签、标签长按/拖动、
双指与滚动/滑块继承、模态窗口外点击关闭而背景不响应、世界工具选目标、
选择起始地块与确认、纯鼠标、失焦及主菜单清理。补记设备、窗口模式、
UI/系统缩放和已加载 DLL 身份。

此次使用原版 1.7 标签布局语义，未承诺改变布局的任意模组均适配；
非窗口 HUD 的完整覆盖、动态 ImmediateWindow 时序和现场鼠标提升时序
仍保留为待验证边界。若出现新的实际漏判，优先利用 `owner` 定向诊断，
不凭“最多一帧”假定引入延后提交队列。

## 用户实测反馈与固定测试目录（2026-11-25）

用户确认已实测，“目前没有相关 bug”，据此关闭本次信息标签穿透/手势归属修复的
验收待办。反馈没有逐项列出设备、窗口模式、缩放及完整回归矩阵，不扩展为任意
模组或未列明场景均已验证。

用户明确要求只保留一份完整本地测试 Mod，每次新编译动态更新，供其直接复制替换；
无需另外创建 ZIP 或任务专属测试目录，也不再保留旧版本。固定目录为
`.build/local/RimTouch/`。`Build17.ps1` 在编译成功后刷新完整目录，编译失败则
保留上一次可用目录；`.build/1.7/` 继续保存原始 DLL 和构建收据。
此前 `.build/ui-touch-ownership/local/RimTouch` 等目录和历史测试 ZIP 已被本轮
清理取代；文档中的旧路径仅作为历史交付记录。项目规则与固定交付入口同步更新。

## 创建世界：随机着陆点与底部按钮归属调查（2026-11-25）

用户观察：触摸随机着陆点按钮后，选中地块位于手指点击 UI 的位置，
鼠标操作则将选中地块置于画面中央。用户怀疑鼠标跟随触点与相机定位冲突。
本轮仅调查，未修改运行源码、构建或安装新 DLL，未启动游戏。

### 已确认的代码证据

- 只读检查本机 `1.7.4995 rev309` 的游戏 IL；游戏程序集哈希与当前构建收据一致。
- `Page_SelectStartingSite.InitialSize` 返回零尺寸；其构造器关闭
  `absorbInputAroundWindow` 和 `preventCameraMotion`。底部按钮由
  `ExtraOnGUI -> DoCustomBottomButtons` 在窗口本体之外绘制，
  仅绘制背景，并未为底部按钮背景另建窗口。
- 随机按钮先设置 `WorldInterface.SelectedTile`，再执行
  `WorldCameraDriver.JumpTo(WorldGrid.GetTileCenter(SelectedTile))`。
  `JumpTo(Vector3)` 由目标方向计算 `sphereRotation`，不读取鼠标/触点位置；
  此按钮分支也没有直接修改缩放高度。
- `UiHitTester.IsLikelyUiPosition` 在 Entry 世界地图中，窗口/Inspect 标签未命中后
  返回 false，没有识别上述底部按钮区。因此按钮起手可归属 `WorldMap`。
- `CompleteActiveGesture` 会把这类短点按直接交给 `DoWorldLeftClick`；
  后者按释放位置射线取地块并调用 `SelectFirstOrNextAt`，不检查 GUI 事件消费状态。
  原版 `HandleWorldClicks` 的抑制补丁不会阻止此直接调用。

### 隔离验证与限制

复用 `scripts/test-ui-touch.ps1` 的测试替身，在内存中加入一次性探针，
编译实际 `UiHitTester`、`UpdateOneFinger` 与 `CompleteActiveGesture`，未修改测试文件。
用说明用的 1280×807 UI、零尺寸且不吸收输入的页面、底部点 `(640,770)`：

- 起手得到 `owner=WorldMap, blocking=false`；即使先消费 GUI 事件，
  释放仍产生 `worldClicks=1, uiTaps=0`。
- 对照：给同一位置一个正常窗口矩形，得到 `owner=Ui, blocking=true`，
  释放为 `worldClicks=0, uiTaps=1`。

这证明底部按钮的 UI 起手漏判及独立地图选点路径真实存在。
现场是否严格发生“按钮先随机并跳转、随后触控释放重选按钮下地块”，
尚无逐帧日志确认，不把隔离探针视为真机时序复现。原有生命周期诊断可确认
现场 `owner`，但没有随机目标、跳转与选中地块变化的完整记录。
相机锚点冲突、原生鼠标尾事件重选、悬浮高亮被误认作选中地块仍未现场证实。

### 待实施方向与验证

最小候选修正为补齐选点页底部实际布局的 UI 起手判断，继续使用现有
`TouchOwner`；覆盖同一绘制入口的按钮，不围绕随机按钮另写相机补偿。
布局需遵循原版单双行排列及 Inspect 面板避让，不能用整条底边兜底。
按钮区外地图与普通非窗口 HUD 的双指入口仍按已有归属规则处理。

待取得一次现场精简诊断/复现顺序后确定修复范围；若需要确认选中地块变化先后，
复用临时有界诊断补充定向记录，不建额外测试界面。
后续应验证随机按钮只选择随机目标并居中、同组按钮无背景选点、
单双行和面板避让布局、按钮旁空白地图点按/拖动、双指及纯鼠标。

### 本次现场精简日志核对

用户提供本地测试记录 `TestReports/26.11.25-1.md`；玩家日志继续保持 Git 忽略，
不复制原始日志入库。读取到的记录共 229 条，有正常 captureStart/captureStop，
屏幕为 1920×1210。日志 assembly 标识与当前本地 DLL 的 MVID 一致；
窗口模式、系统缩放和设备型号仍未记录，不从屏幕尺寸推定。

- gesture 8：frame 3629 在 GUI `(681.3334,774.3198)` 起手，
  `owner=WorldMap, startedOverUi=false`；frame 3630 抬手，位置未变，
  reset 时仍是 `OneFinger/WorldMap`，未取消、未进入 UI 滚动。
- gesture 10：frame 3775–3778，GUI `(680,773.6528)`，同样误归属。
  gesture 12–15 在 GUI x≈628–725、y≈761–786 的底部连续点按也保持
  `OneFinger/WorldMap`，起止位置未变，未记录 abort。
- gesture 7、8 早于 gesture 9 的双指手势（frame 3694–3742），
  因此本次漏判不能以双指缩放后的残留状态解释。
- 设置窗口及其他正常窗口触控记录为 `Ui`，支持缺口集中于窗口外绘制区域，
  不能据此推定所有 UI 命中或坐标换算普遍失效。
- gesture 19 虽从底部 `WorldMap` 起手，但 frame 4390 先记录 abort 再 reset；
  不将它算作正常释放后提交地图点按的证据。

结合原版布局 IL、实际归属/释放代码与隔离对照，现场底部 UI 起手漏判已证实。
正常短点按的状态符合 `CompleteActiveGesture -> DoWorldLeftClick` 分支条件。
现有日志未记录按钮标签、随机目标地块、JumpTo 或最终 selectedTile，
不能直接证明随机目标被哪一帧的哪次选择覆盖，也不能把所有底部触点都命名为
“随机按钮”。最符合现象的因果解释仍是同一触摸同时触发 UI 与直接地图选点。
修正应针对实际 UI 布局及现有归属机制，无证据要求取消鼠标跟手或改写相机居中。
本次继续只更新调查文档与待办，未修改运行源码或构建新 DLL。

### 用户纠正修复范围：共用派发与控件归属

用户指出此前已修复按钮穿透，反对继续逐页、逐按钮枚举区域，要求从共用机制处理
并考虑其他模组兼容性。上文“补齐选点页布局”只是一项局部候选，不作为本轮实施结论；
后续需先验证共用输入归属方案，不能以追加按钮矩形宣称通用修复。

继续只读核对 `1.7.4995` 的 IL，确认：

- `UIRoot_Entry.UIRootOnGUI` 在 `WindowStackOnGUI`（包含各窗口的 `ExtraOnGUI`）
  之后调用世界 `HandleLowPriorityInput`；`UIRoot_Play` 也先派发窗口 GUI，
  再调用地图/世界的低优先级输入。原生派发存在 UI 优先的明确边界。
- 当前 `RootUpdatePatch.Prefix` 调用触控 `Update`，其中释放/触点消失会直接
  `CompleteActiveGesture` 提交选择。它没有等待此次 UI 派发结果；
  `Root.OnGUI` 的现有 Postfix 只处理诊断和触控拖框绘制。
  窗口外控件的几何漏判会经这条独立提交路径转成背景操作。
- 当前 Waylith/Unity 兼容层 `GUI.ButtonImpl` 使用 `_buttonDownActive`、
  `_buttonDownRect` 并消费 MouseDown/MouseUp；`Widgets.ButtonInvisibleDraggable`
  则读取 `Input.GetMouseButton*`，维护自己的 activeControl，不依赖上述状态，
  其 IL 不调用 `Event.Use()`。滑块也有各自的拖动 ID。
- `GUIUtility.hotControl` 在此实现中是简单属性；上述按钮路径没有维护它。
  所以仅检查 hotControl 或最终事件 Used 不能覆盖已确认的这些标准控件。

据此，共用修正的候选约束是：UI 派发前不提交触控地图副作用；通过同次派发的事件
消费及标准交互控件实际接管结果确定归属，然后由点按、长按、拖动、双指和释放
共用同一归属。已归属地图的手势后来经过 UI 不应被夺走。
这属于需隔离验证的设计方向，尚未实现；GUI pass 与原生触控/鼠标消息的对应、
首次按下/抬起同帧、无有效 UI 派发、视图切换及取消必须先验证。
不以跨帧全局矩形缓存或无匹配依据的延迟点击队列代替这些验证。

兼容性判断应按机制区分：复用原生窗口、Widgets/GUI 或消费事件的模组 UI
有望通过共用入口受益，无需枚举模组名；完全自绘、直接轮询 Input 且不消费事件、
不公开捕获状态的控件，无法仅凭鼠标位置可靠识别其语义，不能承诺任意模组零冲突。
回归需包含没有页面专用命中分支的模拟模组控件，证明共用规则生效。

## 点按提交顺序：授权实施与本地测试交付

用户授权正式推进已确认的点按提交顺序修正；“完善共用控件接管机制”单独进入
后续待办，覆盖拖动、长按和双指的起手认领，本轮不扩展为该项重构。
工作分支为 `fix/touch-tap-dispatch`。

### 实现

- 新增 `TouchInputDriver.UiTap.cs`，仅保留当前手势的一份点按状态：
  物理触点 ID、独立序号、起点/释放点、按下/抬起证据及视图/尺寸身份。
  `CompleteActiveGesture` 的地图短点按改为释放意图，保留原有选点、双击、
  征召命令及世界点击实现，统一由提交出口调用。
- 在 Entry/Play 的 `UIRootOnGUI` 调用前后观察对应输入事件。这里位于
  `Root.ApplyUIScale` 之后，记录根 GUI 坐标；不把 `Root.OnGUI` Postfix
  或 Layout/Repaint 当作“所有 GUI pass 已结束”的证明。
- 必须先将鼠标按下与当前物理触点、坐标关联，再取得同序列抬起的 UI 派发结果，
  且物理触摸已释放，才提交一次。支持鼠标边沿先到、触点 Ended 后到，
  及释放意图先到、GUI 边沿后到；不按固定一帧或八帧自动放行。
- 匹配事件被消费即撤销；既有按钮后处理和可拖动按钮 Postfix 也报告实际触发，
  覆盖直接轮询 Input、不调用 Use 的控件。控件命中使用当前局部坐标和实际矩形，
  不增加页面/按钮/模组名单，不保存跨帧全局控件矩形。
- 模态/窗口阻挡在提交时再次遵守原生规则。新手势、新鼠标按下、双指、
  非点按结束、取消/失焦、关闭触控、UIRoot/世界/地图/尺寸改变、工具激活、
  GUI 异常或事件对象被替换都会按对应条件撤销旧意图。
  无有效 UI 派发或无法配对的鼠标抬起不会自动提交；下一手势覆盖旧意图，
  不维护可重放的点击队列。
- 既有手势起手归属不被后续事件反复重算；新的 `mapTapDecision` 诊断记录
  最终提交/撤销，因此 owner=WorldMap 不再等于一定发生背景选点。
  诊断仍复用临时、有界的 Debug log/精简复制入口。

### 验证与交付范围

- 免游戏依赖的卫生、C# 语法、诊断、UI、设置布局及世界数学/交互检查通过；
  UI、设置和世界的行为负对照通过。
- UI 测试编译实际 pending tap、起手/释放/FinishTouch 及 Entry/Play 补丁；
  覆盖窗口外模拟模组控件、随机选点式 UI 动作、未消费事件的可拖动控件、
  两种输入边沿顺序、延后四帧的已配对抬起、多 GUI pass 去重、旧事件遇新手势、
  无关 UI 消费/窗口变化、视图/尺寸/工具/取消/关闭触控、GUI 异常及鼠标。
  四个负对照分别移除 Inspect 命中、恢复提前提交、忽略事件消费、忽略控件认领，
  均被对应行为断言拒绝。
- 世界数学测试保留真实相机、惯性和手势方法；其中点击提交使用测试适配器，
  该套件不作为 UI 时序的证明，时序由上述实际 UiTap 测试单独覆盖。
- `Build17.ps1` 对 `1.7.4995 rev309` 编译通过，0 诊断；
  `test-harmony-patches.ps1` 检查 39/39 补丁目标与参数绑定通过。
- 完整本地测试 Mod 已原地刷新到 `.build/local/RimTouch/`，包含 DLL、
  About、Languages 和许可文件；沿用 test.15 元数据。未生成发布 ZIP、
  安装、启动游戏、提交、推送或发布。
- 最终 DLL SHA256：
  `9078E6CF2E2FF9AEF5416AF175E7B24400A3B860201D13D8229FABEBC9538545`。
  已核对本地测试 DLL、20 个源码文件与构建收据一致，Languages 与源码目录一致。

### 待真机验收与后续

先复测随机着陆点多次点按、双指放大后再次随机、同组按钮和信息标签，
确认 UI 动作不改变背景选择；同时测试普通地图点按/双击、征召命令、世界球外取消、
拖动/长按/双指、窗口外关闭、鼠标切换及失焦/主菜单清理。
沿用设置页临时诊断并复制精简日志，核对 `committed` 与 `uiConsumed/uiControl`；
若出现地图点按无响应，关注 `unpairedMouseUp`，不能用猜测延迟替代现场证据。

隔离测试与编译不证明 Windows 真机消息顺序。完全自绘且既不消费事件、
也不通过现有标准控件入口报告操作的 UI 仍属于明确兼容边界；
拖动/长按/双指的共用控件起手认领保留在后续计划。

### 用户真机验收反馈（2026-11-25）

用户确认：“已测试，目前涉及到的点都改好了。没有明显的bug。”
据此将本轮点按提交顺序修正记为用户实测验收通过，关闭对应验收待办。
用户未逐项列出设备、窗口模式、缩放或完整回归矩阵，不扩大为任意模组兼容结论。
本次仅补充验收记录，不修改运行源码或重新构建。
共用控件对拖动、长按和双指的起手认领仍作为独立后续计划保留；
本次验收不代表该计划已经实施。
