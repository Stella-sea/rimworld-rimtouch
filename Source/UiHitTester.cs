using RimTouch.TouchPlatform;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimTouch
{
    public static partial class TouchInputDriver
    {
        private static bool IsBlockingWindowAt(Vector2 gui)
        {
            WindowStack stack = Find.WindowStack;
            if (stack != null && (!stack.GetsInput(null)
                || stack.GetWindowAt(gui) != null || IsInspectTabAt(stack, gui)))
            {
                return true;
            }
            if (Find.CurrentMap == null)
            {
                return !IsWorldMapActive();
            }
            return false;
        }

        private static bool IsLikelyUiPosition(Vector2 gui)
        {
            if (IsBlockingWindowAt(gui))
            {
                return true;
            }

            if (Current.ProgramState != ProgramState.Playing || !(Find.UIRoot is UIRoot_Play))
            {
                return !IsWorldMapActive();
            }

            float width = UI.screenWidth;
            float height = UI.screenHeight;
            if (gui.y >= height - 38f)
            {
                return true;
            }

            if (Find.ColonistBar != null)
            {
                ColonistBar.Entry entry;
                if (Find.ColonistBar.TryGetEntryAt(gui, out entry))
                {
                    return true;
                }
            }

            float gizmoHeight = GizmoGridDrawer.HeightDrawnRecently;
            if (gizmoHeight > 0.5f
                && gui.y >= height - 38f - gizmoHeight - 8f
                && gui.x <= width - 150f)
            {
                return true;
            }
            if (gui.x >= width - 230f && gui.y >= height - 165f)
            {
                return true;
            }

            float alertsBottom = height - 165f;
            LetterStack letters = Find.LetterStack;
            if (letters != null && letters.LettersListForReading != null && letters.LettersListForReading.Count > 0)
            {
                alertsBottom = letters.LastTopY;
                if (gui.x >= width - 68f
                    && gui.y >= letters.LastTopY - 8f
                    && gui.y <= height - 160f)
                {
                    return true;
                }
            }

            UIRoot_Play root = Find.UIRoot as UIRoot_Play;
            if (root.alerts != null)
            {
                float alertsHeight = root.alerts.AlertsHeight;
                if (alertsHeight > 0.5f
                    && gui.x >= width - 160f
                    && gui.y >= alertsBottom - alertsHeight - 8f
                    && gui.y <= alertsBottom)
                {
                    return true;
                }
            }

            if (Find.CurrentMap == null && !IsWorldMapActive())
            {
                return true;
            }

            return false;
        }

        private static bool IsInspectTabAt(WindowStack stack, Vector2 gui)
        {
            // 1.7 draws these shared labels in ExtraOnGUI, above windowRect.
            IList<Window> windows = stack.Windows;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                IInspectPane pane = windows[i] as IInspectPane;
                if (pane == null || !pane.AnythingSelected
                    || gui.y < pane.PaneTopY - 30f || gui.y >= pane.PaneTopY)
                {
                    continue;
                }

                IEnumerable<InspectTabBase> tabs = pane.CurTabs;
                if (tabs == null) continue;
                float x = InspectPaneUtility.PaneWidthFor(pane) - 72f;
                foreach (InspectTabBase tab in tabs)
                {
                    if (!tab.IsVisible || tab.Hidden) continue;
                    if (new Rect(x, pane.PaneTopY - 30f, 72f, 30f).Contains(gui))
                    {
                        return true;
                    }
                    x -= 72f;
                }
            }
            return false;
        }

        private static TouchOwner GetTouchOwner(Vector2 gui)
        {
            if (IsLikelyUiPosition(gui)) return TouchOwner.Ui;
            if (IsWorldMapActive())
            {
                return IsWorldMapToolActive() ? TouchOwner.WorldMapTool : TouchOwner.WorldMap;
            }
            return IsVanillaMapToolActive() ? TouchOwner.LocalMapTool : TouchOwner.LocalMap;
        }

        private static bool IsWorldMapToolActive()
        {
            return IsWorldMapActive() && Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting;
        }

        private static bool IsVanillaMapToolActive()
        {
            // Find.DesignatorManager / Find.Targeter go through Find.MapUI,
            // which casts Find.UIRoot to UIRoot_Play in 1.7 - that throws
            // InvalidCastException on Entry screens (main menu, world
            // creation / landing-site selection) and kills the whole touch
            // update, so one-finger touches there do nothing at all (drag
            // and tap alike). No map tool can be active without a running
            // game anyway, so bail out the same way GetLocalSelector() does.
            if (Current.ProgramState != ProgramState.Playing || !(Find.UIRoot is UIRoot_Play))
            {
                return false;
            }
            if (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null)
            {
                return true;
            }
            if (Find.Targeter != null && Find.Targeter.IsTargeting)
            {
                return true;
            }
            return false;
        }

        private static bool HasCurrentOneFingerLocalMapTouch()
        {
            try
            {
                if (TouchSource.Count != 1)
                {
                    return false;
                }

                if (Find.CurrentMap == null || IsWorldMapActive())
                {
                    return false;
                }

                return gestureOwner == TouchOwner.LocalMap;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCancelVanillaMapToolFromTwoFingerTap()
        {
            if (!twoFingerStartedWithVanillaMapTool || Find.CurrentMap == null || IsWorldMapActive())
            {
                return false;
            }

            if (!IsTwoFingerTapGesture())
            {
                return false;
            }

            if (!CancelVanillaMapTool())
            {
                return false;
            }

            suppressReleaseTap = true;
            SuppressUiClickFallout();
            TouchTapRepair.Clear();
            CancelRimWorldDragBox();
            return true;
        }

        private static bool CancelVanillaMapTool()
        {
            bool canceled = false;

            if (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null)
            {
                Find.DesignatorManager.Deselect();
                canceled = true;
            }

            if (Find.Targeter != null && Find.Targeter.IsTargeting)
            {
                Find.Targeter.StopTargeting();
                canceled = true;
            }

            return canceled;
        }

        private static void SuppressUiClickFallout()
        {
            suppressWindowCloseUntilFrame = Math.Max(suppressWindowCloseUntilFrame, Time.frameCount + 12);
            if (TouchSource.Count >= 2 || mode == TouchMode.TwoFinger)
            {
                suppressUiClickUntilFrame = Math.Max(suppressUiClickUntilFrame, Time.frameCount + 12);
            }
        }

        private static void SuppressVanillaMapInputFallout()
        {
            suppressVanillaMapInputUntilFrame = Math.Max(suppressVanillaMapInputUntilFrame, Time.frameCount + 12);
        }

        private static void SuppressUiScrollFallout()
        {
            suppressUiClickUntilFrame = Math.Max(suppressUiClickUntilFrame, Time.frameCount + 12);
            suppressWindowCloseUntilFrame = Math.Max(suppressWindowCloseUntilFrame, Time.frameCount + 12);
        }

        private static Vector2 TouchToGui(Vector2 touchPosition)
        {
            float scaleX = Screen.width > 0 ? UI.screenWidth / (float)Screen.width : 1f;
            float scaleY = Screen.height > 0 ? UI.screenHeight / (float)Screen.height : 1f;
            return new Vector2(touchPosition.x * scaleX, (Screen.height - touchPosition.y) * scaleY);
        }

        private static Vector2 TouchDeltaToGui(Vector2 touchDelta)
        {
            float scaleX = Screen.width > 0 ? UI.screenWidth / (float)Screen.width : 1f;
            float scaleY = Screen.height > 0 ? UI.screenHeight / (float)Screen.height : 1f;
            return new Vector2(touchDelta.x * scaleX, -touchDelta.y * scaleY);
        }

        internal static Vector2 GuiToLocalGuiPoint(Vector2 gui)
        {
            // Anchor to the current event, whose position already includes window,
            // group and scroll offsets. Convert only the displacement: Waylith's
            // absolute ScreenToGUIPoint offset is not scaled with GUI.matrix.
            Event current = Event.current;
            if (current == null) throw new InvalidOperationException("No GUI event for touch hit test.");
            Vector3 mouse = Input.mousePosition;
            Vector2 mouseScreen = new Vector2(mouse.x, Screen.height - mouse.y);
            return current.mousePosition
                + GUIUtility.ScreenToGUIPoint(GuiToScreenGuiPoint(gui))
                - GUIUtility.ScreenToGUIPoint(mouseScreen);
        }

        private static Vector2 GuiToScreenGuiPoint(Vector2 gui)
        {
            float scaleX = UI.screenWidth > 0 ? Screen.width / (float)UI.screenWidth : 1f;
            float scaleY = UI.screenHeight > 0 ? Screen.height / (float)UI.screenHeight : 1f;
            return new Vector2(gui.x * scaleX, gui.y * scaleY);
        }

        private static bool SameRect(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) <= 0.5f
                && Mathf.Abs(a.y - b.y) <= 0.5f
                && Mathf.Abs(a.width - b.width) <= 0.5f
                && Mathf.Abs(a.height - b.height) <= 0.5f;
        }

        private static Vector3 GuiToScreenPoint(Vector2 gui)
        {
            float scaleX = UI.screenWidth > 0 ? Screen.width / (float)UI.screenWidth : 1f;
            float scaleY = UI.screenHeight > 0 ? Screen.height / (float)UI.screenHeight : 1f;
            return new Vector3(gui.x * scaleX, (UI.screenHeight - gui.y) * scaleY, 0f);
        }

        private static Vector3 GuiToMapPosition(Vector2 gui)
        {
            return UI.UIToMapPosition(new Vector2(gui.x, UI.screenHeight - gui.y));
        }
    }
}
