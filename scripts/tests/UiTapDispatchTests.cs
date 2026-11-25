using System;
using UnityEngine;
using Verse;
using RimTouch.TouchPlatform;

namespace RimTouch
{
    public static partial class TouchInputDriver
    {
        private static Vector2 TapTestPoint;

        private static void ResetTapCase(bool world)
        {
            CancelMapTap("testReset");
            TapTestPoint = new Vector2(680, world ? 774 : 400);
            worldActive = world;
            Current.ProgramState = world ? ProgramState.Entry : ProgramState.Playing;
            Find.UIRoot = world ? (UIRoot)new UIRoot_Entry() : new RimWorld.UIRoot_Play();
            Find.CurrentMap = world ? null : new Map();
            Find.World = new object();
            Find.WindowStack.windows.Clear();
            Find.WindowStack.currentlyDrawnWindow = null;
            Find.DesignatorManager.SelectedDesignator = null;
            Find.Targeter.IsTargeting = Find.WorldTargeter.IsTargeting = false;
            Screen.width = 1920; Screen.height = 1210;
            UI.screenWidth = 1280; UI.screenHeight = 807;
            GUIUtility.scale = 1.5f;
            GUIUtility.offset = GUIUtility.groupOffset = Vector2.zero;
            mode = TouchMode.None;
            TouchModeEnabled = true;
            TouchSource.Canceled = ignoreTouchesUntilAllReleased = uiScrollGestureActive = false;
            TouchSource.Count = 0;
            lastMultiTouchFrame = suppressUiClickUntilFrame = -1000;
            localClicks = worldClicks = 0;
            Input.mouseDown = Input.mouseUp = Input.mouseHeld = false;
        }

        private static void TapGui(EventType type, Vector2 point, Action ui = null)
        {
            Input.mousePosition = new Vector3(point.x * Screen.width / UI.screenWidth,
                Screen.height - point.y * Screen.height / UI.screenHeight);
            Event.current = new Event { type = type, button = 0, mousePosition = point };
            TouchTapGuiState state;
            if (worldActive) EntryTouchTapDispatchPatch.Prefix(out state);
            else PlayTouchTapDispatchPatch.Prefix(out state);
            if (ui != null) ui();
            if (worldActive) EntryTouchTapDispatchPatch.Postfix(state);
            else PlayTouchTapDispatchPatch.Postfix(state);
        }

        private static void StartTestTap()
        {
            Finger(TapTestPoint.x, TapTestPoint.y, ContactPhase.Began);
            Assert(!startedOverUi, "test control unexpectedly had a page-specific UI hit");
        }

        private static void ReleaseTestTap()
        {
            Finger(TapTestPoint.x, TapTestPoint.y, ContactPhase.Ended);
        }

        public static void RunTapDispatchTests()
        {
            foreach (bool world in new[] { true, false })
            {
                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                ReleaseTestTap();
                Assert(localClicks + worldClicks == 0, "tap committed before UI");
                TapGui(EventType.Layout, TapTestPoint);
                TapGui(EventType.Repaint, TapTestPoint);
                Assert(localClicks + worldClicks == 0, "non-input GUI pass committed a map tap");
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert((world ? worldClicks : localClicks) == 1, "unclaimed tap did not commit");
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 1, "GUI pass duplicated map tap");

                // UI control is drawn outside all windows, with no page/type hit rule.
                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint, () => Event.current.Use());
                ReleaseTestTap();
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 0, "consumed UI press leaked to map");

                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                ReleaseTestTap();
                int randomSelections = 0;
                TapGui(EventType.MouseUp, TapTestPoint, () => { randomSelections++; Event.current.Use(); });
                Assert(randomSelections == 1 && localClicks + worldClicks == 0,
                    "random-like UI action also selected background");

                // A native draggable can fire on Layout without consuming the event.
                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                ReleaseTestTap();
                Input.mouseUp = true;
                TapGui(EventType.Layout, TapTestPoint, () =>
                {
                    Event.current.mousePosition = new Vector2(20, 20);
                    WidgetsButtonInvisibleDraggableTouchPatch.Postfix(
                        new Rect(10, 10, 40, 40), Widgets.DraggableResult.Pressed);
                });
                Input.mouseUp = false;
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 0, "non-consuming control leaked to map");

                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 0, "mouse-up committed before physical release");
                ReleaseTestTap();
                Assert(localClicks + worldClicks == 1, "same-frame mouse edges lost the physical tap");

                ResetTapCase(world);
                StartTestTap();
                ReleaseTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 1, "release-before-GUI ordering lost tap");

                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                ReleaseTestTap();
                TouchSource.Count = 0;
                Time.frameCount += 4;
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 1, "paired late mouse-up was forced into a one-frame limit");

                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                TapGui(EventType.MouseUp, new Vector2(100, 100), () => Event.current.Use());
                TapGui(EventType.KeyDown, TapTestPoint, () => Event.current.Use());
                ReleaseTestTap();
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 1, "unrelated UI activity swallowed a map tap");

                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                ReleaseTestTap();
                TapGui(EventType.MouseUp, TapTestPoint, () =>
                    Find.WindowStack.windows.Add(new Window { windowRect = new Rect(10, 10, 100, 100) }));
                Assert(localClicks + worldClicks == 1, "unrelated window change swallowed map tap");

                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                ReleaseTestTap();
                TapGui(EventType.MouseUp, TapTestPoint, () =>
                    Finger(TapTestPoint.x, TapTestPoint.y, ContactPhase.Began));
                Assert(localClicks + worldClicks == 0, "old GUI dispatch submitted a new gesture");
                TapGui(EventType.MouseDown, TapTestPoint);
                ReleaseTestTap();
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 1, "new gesture was consumed by old GUI dispatch");

                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                ReleaseTestTap();
                StartTestTap();
                TapGui(EventType.MouseUp, TapTestPoint, () =>
                    WidgetsButtonInvisibleDraggableTouchPatch.Postfix(
                        new Rect(TapTestPoint.x - 10, TapTestPoint.y - 10, 20, 20), Widgets.DraggableResult.Pressed));
                Assert(localClicks + worldClicks == 0, "previous gesture mouse-up submitted the new gesture");
                TapGui(EventType.MouseDown, TapTestPoint);
                ReleaseTestTap();
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 1, "replacement gesture did not commit once");

                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                ReleaseTestTap();
                TouchSource.Count = 0;
                TapGui(EventType.MouseDown, TapTestPoint);
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 0, "new mouse click revived an old touch");

                foreach (string change in new[] { "root", "world", "map", "size", "scale", "disabled", "canceled", "tool", "modal", "exception", "event" })
                {
                    ResetTapCase(world);
                    StartTestTap();
                    TapGui(EventType.MouseDown, TapTestPoint);
                    ReleaseTestTap();
                    switch (change)
                    {
                        case "root": Find.UIRoot = new UIRoot(); break;
                        case "world": Find.World = new object(); break;
                        case "map": Find.CurrentMap = new Map(); break;
                        case "size": Screen.width++; break;
                        case "scale": UI.screenWidth++; break;
                        case "disabled": TouchModeEnabled = false; break;
                        case "canceled": TouchSource.Canceled = true; break;
                        case "tool":
                            if (world) Find.WorldTargeter.IsTargeting = true;
                            else Find.Targeter.IsTargeting = true;
                            break;
                        case "modal": Find.WindowStack.windows.Add(new Window { absorbInputAroundWindow = true }); break;
                        case "exception":
                            if (world) EntryTouchTapDispatchPatch.Finalizer(new Exception("test"));
                            else PlayTouchTapDispatchPatch.Finalizer(new Exception("test"));
                            break;
                    }
                    if (change == "event")
                        TapGui(EventType.MouseUp, TapTestPoint, () => Event.current = new Event { type = EventType.MouseUp, mousePosition = TapTestPoint });
                    else
                        TapGui(EventType.MouseUp, TapTestPoint);
                    Assert(localClicks + worldClicks == 0, change + " retained a pending map tap");
                }

                ResetTapCase(world);
                StartTestTap();
                TapGui(EventType.MouseDown, TapTestPoint);
                suppressReleaseTap = true;
                ReleaseTestTap();
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 0, "suppressed gesture left a pending tap");

                ResetTapCase(world);
                TapGui(EventType.MouseDown, TapTestPoint);
                TapGui(EventType.MouseUp, TapTestPoint);
                Assert(localClicks + worldClicks == 0, "pure mouse input created a touch tap");
            }
            Console.WriteLine("PASS UI tap dispatch: UI-first, unregistered mod controls, both edge orderings, paired delayed release, gesture identity, cancellation and mouse fallback");
        }
    }
}
