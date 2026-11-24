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
