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
    internal enum ScrollDecision
    {
        Disabled, NotScrollable, Inertia, BlockedContacts, Ended, OtherFinger,
        Active, OtherRect, OtherWindow, Claimed, Outside, BelowSlop
    }

    public static partial class TouchInputDriver
    {
        private static readonly VelocityHistory UiScrollVelocityHistory = new VelocityHistory(5, 0.12f);
        private static Window uiTouchStartWindow;
        private static Vector2 uiTouchStartWindowPosition;
        private static Window uiScrollWindow;
        private static Window uiScrollInertiaWindow;
        private static Window uiScrollCandidateWindow;
        private static long uiScrollCandidateFingerId = -1;
        private static Vector2 uiScrollCandidateStartGui;
        private static Window uiScrollCandidateStartWindow;
        private static float uiScrollCandidateArea = float.PositiveInfinity;
        private static Vector2 uiScrollCandidateMaximum;
        private static Vector2 uiScrollCandidatePosition;
        private static bool uiScrollCandidateDeferred;
        private static float uiScrollCandidateDeferredArea;
        private static readonly FieldInfo DraggingWindowIdField = ReflectionGuard.Field(typeof(GUI), "_draggingWindowId", false);
        private static readonly FieldInfo CurrentWindowIdField = ReflectionGuard.Field(typeof(GUI), "_currentWindowId", false);

        public static bool ShouldAllowWindowDrag()
        {
            if (!TouchModeEnabled) return true;
            Window drawn = Find.WindowStack == null ? null : Find.WindowStack.currentlyDrawnWindow;
            // Reserve only a scrollable viewport touched from inside. Window edges
            // remain native drags; an existing window drag must not run on Repaint
            // after MouseDrag was consumed (Waylith updates it on every GUI pass).
            if (drawn == null || (drawn != uiScrollWindow && drawn != uiScrollCandidateWindow)) return true;
            int draggingId, currentId;
            if (ReflectionGuard.TryGetField(DraggingWindowIdField, null, out draggingId)
                && ReflectionGuard.TryGetField(CurrentWindowIdField, null, out currentId)
                && draggingId == currentId)
            {
                // Optional Waylith cleanup: a previously captured drag otherwise
                // survives a skipped MouseUp. Other renderers have no such fields.
                ReflectionGuard.TrySetField(DraggingWindowIdField, null, -1);
            }
            return false;
        }

        public static void FilterHorizontalSliderDrag()
        {
            Event current = Event.current;
            if (!TouchModeEnabled || current == null || current.type != EventType.MouseDrag
                || TouchSource.Count != 1 || uiScrollCandidateWindow == null
                || Find.WindowStack == null || uiScrollCandidateWindow != Find.WindowStack.currentlyDrawnWindow) return;
            Vector2 drag = TouchToGui(TouchSource.GetTouch(0).position) - startGui;
            if (uiScrollGestureActive || Mathf.Abs(drag.x) <= MovementDetectionSlopPixels
                || Mathf.Abs(drag.x) <= Mathf.Abs(drag.y) * 1.5f)
            {
                // While direction is undecided, draw the original slider but do
                // not let tiny horizontal jitter change its value during a swipe.
                current.Use();
            }
        }

        public static bool ProcessScrollViewTouch(Rect outRect, Rect viewRect, ref Vector2 scrollPosition)
        {
            ScrollDecision decision;
            if (!TouchInputProof.IsRecording)
            {
                return ProcessScrollViewTouchCore(outRect, viewRect, ref scrollPosition, out decision);
            }
            Vector2 before = scrollPosition;
            bool wasActive = uiScrollGestureActive;
            bool wasInertia = uiScrollInertiaActive;
            Vector2 previousScrollGui = uiScrollLastGui;
            ScrollProofEvent inputEvent = CaptureScrollProofEvent();
            bool handled = ProcessScrollViewTouchCore(outRect, viewRect, ref scrollPosition, out decision);
            RecordScrollDecision(outRect, viewRect, before, scrollPosition, wasActive, wasInertia,
                previousScrollGui, inputEvent, handled, decision);
            return handled;
        }

        private static bool ProcessScrollViewTouchCore(Rect outRect, Rect viewRect, ref Vector2 scrollPosition,
            out ScrollDecision decision)
        {
            decision = ScrollDecision.Disabled;
            if (!TouchModeEnabled)
            {
                return false;
            }

            float maxX = Mathf.Max(0f, viewRect.width - outRect.width);
            float maxY = Mathf.Max(0f, viewRect.height - outRect.height);
            if (maxX <= 1f && maxY <= 1f)
            {
                decision = ScrollDecision.NotScrollable;
                return false;
            }

            if (TouchSource.Count == 0)
            {
                decision = ScrollDecision.Inertia;
                if (uiScrollInertiaActive && Time.realtimeSinceStartup - uiScrollInertiaStartTime > 1.25f)
                {
                    StopUiScrollInertia();
                    return false;
                }
                return ApplyUiScrollInertia(outRect, maxX, maxY, ref scrollPosition);
            }

            if (TouchSource.Count != 1 || ignoreTouchesUntilAllReleased)
            {
                decision = ScrollDecision.BlockedContacts;
                return false;
            }

            ContactTouch touch;
            try
            {
                touch = TouchSource.GetTouch(0);
            }
            catch
            {
                decision = ScrollDecision.BlockedContacts;
                return false;
            }

            if (touch.phase == ContactPhase.Ended)
            {
                decision = ScrollDecision.Ended;
                return false;
            }

            if (primaryFingerId < 0 || touch.fingerId != primaryFingerId)
            {
                decision = ScrollDecision.OtherFinger;
                return false;
            }

            Vector2 gui = TouchToGui(touch.position);
            Vector2 reportedGuiDelta = TouchDeltaToGui(touch.deltaPosition);
            Vector2 previousGui = uiScrollGestureActive ? uiScrollLastGui : gui - reportedGuiDelta;
            if (!uiScrollGestureActive)
            {
                Window drawn = Find.WindowStack == null ? null : Find.WindowStack.currentlyDrawnWindow;
                if (drawn != uiTouchStartWindow)
                {
                    decision = ScrollDecision.OtherWindow;
                    return false;
                }
                Rect contentRect = outRect;
                // Keep the original scrollbar thumb/track path (16 GUI units).
                if (maxY > 1f) contentRect.width = Mathf.Max(0f, contentRect.width - 16f);
                if (maxX > 1f) contentRect.height = Mathf.Max(0f, contentRect.height - 16f);
                if (!IsTouchOverScrollRect(contentRect))
                {
                    decision = ScrollDecision.Outside;
                    return false;
                }

                ObserveUiScrollCandidate(drawn, outRect, maxX, maxY, scrollPosition);
                uiScrollCandidateWindow = drawn;
                // Distance alone must never steal a horizontal slider drag.
                Vector2 dragFromStart = gui - startGui;
                bool directionClaim = IsScrollDirectionDrag(maxX, maxY, dragFromStart);
                decision = ScrollDecision.BelowSlop;
                if (!directionClaim)
                {
                    return false;
                }

                if (!HasScrollHeadroom(maxX, maxY, scrollPosition, dragFromStart)
                    || DeferToInnerScrollView(outRect, dragFromStart))
                {
                    return false;
                }

                StopUiScrollInertia();
                ClaimTouchForUiScroll();
                uiScrollGestureActive = true;
                uiScrollWindow = drawn;
                uiScrollActiveRect = outRect;
                uiScrollLastGui = previousGui;
                lastUiScrollSampleTime = Time.realtimeSinceStartup;
                decision = ScrollDecision.Claimed;
            }
            else if (uiScrollWindow != (Find.WindowStack == null ? null : Find.WindowStack.currentlyDrawnWindow)
                || !SameRect(uiScrollActiveRect, outRect))
            {
                decision = ScrollDecision.OtherRect;
                return false;
            }
            else
            {
                decision = ScrollDecision.Active;
            }

            SuppressUiScrollFallout();
            suppressReleaseTap = true;
            TouchTapRepair.Clear();

            // A scroll gesture must not also drive the control under the finger: if
            // the drag starts on a slider, the slider would otherwise read the same
            // mouse-drag and change its value while the view scrolls. Consume
            // drags and delayed presses after claiming, but preserve MouseUp
            // so native controls can release their capture.
            Event currentEvent = Event.current;
            if (currentEvent != null && (currentEvent.type == EventType.MouseDrag || currentEvent.type == EventType.MouseDown))
            {
                currentEvent.Use();
            }

            if (lastUiScrollApplyFrame == Time.frameCount)
            {
                return true;
            }

            Vector2 guiDelta = gui - uiScrollLastGui;
            if (guiDelta.sqrMagnitude <= 0.01f)
            {
                uiScrollLastGui = gui;
                return true;
            }

            Vector2 scrollDelta = Vector2.zero;
            if (maxX > 1f)
            {
                scrollDelta.x = -guiDelta.x;
                scrollPosition.x = Mathf.Clamp(scrollPosition.x + scrollDelta.x, 0f, maxX);
            }
            if (maxY > 1f)
            {
                scrollDelta.y = -guiDelta.y;
                scrollPosition.y = Mathf.Clamp(scrollPosition.y + scrollDelta.y, 0f, maxY);
            }

            SampleUiScrollVelocity(scrollDelta);
            uiScrollLastGui = gui;
            lastUiScrollApplyFrame = Time.frameCount;
            return true;
        }

        private static void ObserveUiScrollCandidate(Window drawn, Rect outRect, float maxX, float maxY,
            Vector2 scrollPosition)
        {
            if (uiScrollCandidateFingerId != primaryFingerId
                || (uiScrollCandidateStartGui - startGui).sqrMagnitude > 0f
                || uiScrollCandidateStartWindow != uiTouchStartWindow
                || (uiScrollCandidateWindow == null && drawn != null))
            {
                ResetUiScrollCandidate();
                uiScrollCandidateFingerId = primaryFingerId;
                uiScrollCandidateStartGui = startGui;
                uiScrollCandidateStartWindow = uiTouchStartWindow;
            }

            // Areas remain comparable across nested local origins. Do not compare
            // local rectangles or borrow the mouse position to identify the inner view.
            float area = outRect.width * outRect.height;
            if (area <= uiScrollCandidateArea + 1f)
            {
                uiScrollCandidateArea = area;
                uiScrollCandidateMaximum = new Vector2(maxX, maxY);
                uiScrollCandidatePosition = scrollPosition;
            }
        }

        private static bool DeferToInnerScrollView(Rect outRect, Vector2 dragFromStart)
        {
            float area = outRect.width * outRect.height;
            if (uiScrollCandidateArea >= area - 1f
                || !IsScrollDirectionDrag(uiScrollCandidateMaximum.x, uiScrollCandidateMaximum.y, dragFromStart)
                || !HasScrollHeadroom(uiScrollCandidateMaximum.x, uiScrollCandidateMaximum.y,
                    uiScrollCandidatePosition, dragFromStart))
            {
                return false;
            }

            // BeginScrollView runs outside-in. Each smaller ancestor can defer
            // once in this pass. Seeing an equal/larger ancestor again means the
            // inner view did not claim last pass (or disappeared): let it hand off.
            if (uiScrollCandidateDeferred && area >= uiScrollCandidateDeferredArea - 1f)
            {
                return false;
            }
            uiScrollCandidateDeferred = true;
            uiScrollCandidateDeferredArea = area;
            return true;
        }

        private static void ResetUiScrollCandidate()
        {
            uiScrollCandidateFingerId = -1;
            uiScrollCandidateStartGui = Vector2.zero;
            uiScrollCandidateStartWindow = null;
            uiScrollCandidateArea = float.PositiveInfinity;
            uiScrollCandidateMaximum = Vector2.zero;
            uiScrollCandidatePosition = Vector2.zero;
            uiScrollCandidateDeferred = false;
            uiScrollCandidateDeferredArea = 0f;
        }

        private static bool HasScrollHeadroom(float maxX, float maxY, Vector2 scrollPosition,
            Vector2 dragFromStart)
        {
            if (Mathf.Abs(dragFromStart.y) > Mathf.Abs(dragFromStart.x))
            {
                return maxY > 1f && (dragFromStart.y > 0f
                    ? scrollPosition.y > 0.5f : scrollPosition.y < maxY - 0.5f);
            }
            return maxX > 1f && (dragFromStart.x > 0f
                ? scrollPosition.x > 0.5f : scrollPosition.x < maxX - 0.5f);
        }

        private static bool IsTouchOverScrollRect(Rect outRect)
        {
            // Strict viewport and start point in the same local coordinate space.
            // Never borrow a stale mouse position or compare global points to it.
            try
            {
                return outRect.Contains(ScrollStartLocal());
            }
            catch
            {
                return false;
            }
        }

        private static Vector2 ScrollStartLocal()
        {
            Vector2 gui = startGui;
            if (uiTouchStartWindow != null)
            {
                // Keep eligibility at the press, even if a native edge drag has
                // since moved the window beneath the original screen point.
                gui += new Vector2(uiTouchStartWindow.windowRect.x, uiTouchStartWindow.windowRect.y)
                    - uiTouchStartWindowPosition;
            }
            return GuiToLocalGuiPoint(gui);
        }

        // True when the drag from the touch start is clearly a scroll along
        // one of this view's scrollable axes, so the scroll can be claimed
        // before a slider under the finger can treat the swipe as its own drag.
        // The drag must be mostly along a
        // scrollable axis (dominant component at least 1.5x the other) and
        // past the small movement slop, so a deliberate slider drag along
        // the slider's own axis is never stolen.
        private static bool IsScrollDirectionDrag(float maxX, float maxY, Vector2 dragFromStart)
        {
            float absX = Mathf.Abs(dragFromStart.x);
            float absY = Mathf.Abs(dragFromStart.y);
            if (maxY > 1f && absY > MovementDetectionSlopPixels && absY > absX * 1.5f)
            {
                return true;
            }
            if (maxX > 1f && absX > MovementDetectionSlopPixels && absX > absY * 1.5f)
            {
                return true;
            }
            return false;
        }

        private static void ClaimTouchForUiScroll()
        {
            startedOverUi = true;
            suppressReleaseTap = true;

            if (mode == TouchMode.SelectionHold)
            {
                CancelRimWorldDragBox();
            }

            if (mode == TouchMode.MapPan || mode == TouchMode.SelectionHold)
            {
                mode = TouchMode.OneFinger;
            }

            CancelRimWorldDragBox();
            CancelWorldDragBox();
            SuppressUiScrollFallout();
        }

        private static bool ApplyUiScrollInertia(Rect outRect, float maxX, float maxY, ref Vector2 scrollPosition)
        {
            if (!uiScrollInertiaActive || !SameRect(uiScrollInertiaRect, outRect)
                || uiScrollInertiaWindow != (Find.WindowStack == null ? null : Find.WindowStack.currentlyDrawnWindow))
            {
                return false;
            }

            if (uiScrollVelocity.sqrMagnitude <= MinUiScrollInertiaSpeed * MinUiScrollInertiaSpeed)
            {
                StopUiScrollInertia();
                return false;
            }

            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Clamp(now - lastUiScrollInertiaTime, 0.001f, 0.05f);
            lastUiScrollInertiaTime = now;

            if (lastUiScrollInertiaApplyFrame == Time.frameCount)
            {
                SuppressUiScrollFallout();
                return true;
            }

            Vector2 before = scrollPosition;
            if (maxX > 1f)
            {
                scrollPosition.x = Mathf.Clamp(scrollPosition.x + uiScrollVelocity.x * dt, 0f, maxX);
            }
            else
            {
                uiScrollVelocity.x = 0f;
            }
            if (maxY > 1f)
            {
                scrollPosition.y = Mathf.Clamp(scrollPosition.y + uiScrollVelocity.y * dt, 0f, maxY);
            }
            else
            {
                uiScrollVelocity.y = 0f;
            }

            if ((scrollPosition - before).sqrMagnitude <= 0.0001f)
            {
                StopUiScrollInertia();
                return false;
            }

            uiScrollVelocity *= Mathf.Exp(-PanInertiaDamping * 0.75f * dt);
            lastUiScrollInertiaApplyFrame = Time.frameCount;
            SuppressUiScrollFallout();
            return true;
        }

        private static void FinalizeUiScrollInertia()
        {
            if (!uiScrollGestureActive)
            {
                return;
            }

            Vector2 recentVelocity = UiScrollVelocityHistory.HasSamples
                ? UiScrollVelocityHistory.GetWeightedAverage2D(Time.realtimeSinceStartup) : uiScrollVelocity;
            if (recentVelocity.magnitude < MinUiScrollInertiaStartSpeed)
            {
                StopUiScrollInertia();
                return;
            }

            uiScrollVelocity = recentVelocity;
            uiScrollInertiaActive = true;
            uiScrollInertiaRect = uiScrollActiveRect;
            uiScrollInertiaWindow = uiScrollWindow;
            uiScrollInertiaStartTime = Time.realtimeSinceStartup;
            lastUiScrollInertiaTime = uiScrollInertiaStartTime;
            lastUiScrollInertiaApplyFrame = -1000;
        }

        private static void StopUiScrollInertia()
        {
            UiScrollVelocityHistory.Clear();
            ResetUiScrollCandidate();
            uiScrollVelocity = Vector2.zero;
            uiScrollInertiaActive = false;
            uiScrollInertiaWindow = null;
            uiScrollInertiaRect = default(Rect);
            uiScrollInertiaStartTime = 0f;
            lastUiScrollInertiaApplyFrame = -1000;
        }

        private static void SampleUiScrollVelocity(Vector2 scrollDelta)
        {
            float now = Time.realtimeSinceStartup;
            float dt = now - lastUiScrollSampleTime;
            lastUiScrollSampleTime = now;

            if (dt <= 0.0001f || dt > 0.18f || scrollDelta.sqrMagnitude <= 0.01f)
            {
                return;
            }

            Vector2 velocity = scrollDelta / dt;
            if (!IsFinite(velocity))
            {
                return;
            }

            if (velocity.magnitude > MaxUiScrollInertiaSpeed)
            {
                velocity = velocity.normalized * MaxUiScrollInertiaSpeed;
            }

            UiScrollVelocityHistory.Record2D(velocity, now);
            uiScrollVelocity = UiScrollVelocityHistory.GetWeightedAverage2D(now);
        }
    }
}
