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
        Active, OtherRect, OtherWindow, Claimed, Outside, BelowSlop, ControlLocked
    }

    internal enum UiControlDragAxis
    {
        None, Horizontal, Vertical
    }

    internal enum UiSliderArbitrationState
    {
        None, Pending, Committed
    }

    internal enum UiSliderKind
    {
        None, WidgetsHorizontal, WidgetsRange, GuiSlider
    }

    public struct SliderTouchState
    {
        internal bool Active;
        internal bool RestoreValue;
        internal bool CapturedKnown;
        internal int BeforeDragId;
        internal int ExplicitId;
        internal UiSliderKind Kind;
        internal UiControlDragAxis Axis;
        internal Rect HitRect;
        internal Window Window;
    }

    public struct FloatRangeTouchState
    {
        internal SliderTouchState Slider;
        internal FloatRange OriginalValue;
    }

    public struct IntRangeTouchState
    {
        internal SliderTouchState Slider;
        internal IntRange OriginalValue;
    }

    public struct GuiSliderTouchState
    {
        internal SliderTouchState Slider;
        internal float OriginalValue;
    }

    public static partial class TouchInputDriver
    {
        private const float DiagonalCommitDistancePixels = MovementDetectionSlopPixels * 2f;
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
        private static UiSliderArbitrationState uiSliderState;
        private static UiControlDragAxis uiSliderAxis;
        private static Window uiSliderWindow;
        private static UiSliderKind uiSliderCapturedKind;
        private static int uiSliderCapturedId;
        private static readonly FieldInfo DraggingWindowIdField = ReflectionGuard.Field(typeof(GUI), "_draggingWindowId", false);
        private static readonly FieldInfo CurrentWindowIdField = ReflectionGuard.Field(typeof(GUI), "_currentWindowId", false);
        private static readonly FieldInfo WidgetsSliderDraggingIdField = ReflectionGuard.Field(typeof(Widgets), "sliderDraggingID", false);
        private static readonly FieldInfo WidgetsRangeDraggingIdField = ReflectionGuard.Field(typeof(Widgets), "draggingId", false);
        private static readonly FieldInfo WidgetsRangeCurDragEndField = ReflectionGuard.Field(typeof(Widgets), "curDragEnd", false);
        private static readonly FieldInfo GuiSliderDragIdField = ReflectionGuard.Field(typeof(GUI), "_guiSliderDragId", false);
        private static readonly MethodInfo WidgetsResetSliderDraggingIdsMethod = ReflectionGuard.Method(typeof(Widgets), "ResetSliderDraggingIDs", false);

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

        private static void ResetUiSliderArbitration()
        {
            uiSliderState = UiSliderArbitrationState.None;
            uiSliderAxis = UiControlDragAxis.None;
            uiSliderWindow = null;
            uiSliderCapturedKind = UiSliderKind.None;
            uiSliderCapturedId = 0;
        }

        private static void ClearActiveSliderDrags()
        {
            if (!ReflectionGuard.TryInvoke(WidgetsResetSliderDraggingIdsMethod, null, null))
            {
                ReflectionGuard.TrySetField(WidgetsSliderDraggingIdField, null, 0);
                ReflectionGuard.TrySetField(WidgetsRangeDraggingIdField, null, 0);
                if (WidgetsRangeCurDragEndField != null && WidgetsRangeCurDragEndField.FieldType.IsEnum)
                {
                    ReflectionGuard.TrySetField(WidgetsRangeCurDragEndField, null, Enum.ToObject(WidgetsRangeCurDragEndField.FieldType, 0));
                }
            }
            int guiDragId;
            if (ReflectionGuard.TryGetField(GuiSliderDragIdField, null, out guiDragId) && guiDragId != 0)
            {
                ReflectionGuard.TrySetField(GuiSliderDragIdField, null, 0);
            }
        }

        private static bool TryReadSliderDragId(UiSliderKind kind, out int id)
        {
            id = 0;
            switch (kind)
            {
                case UiSliderKind.WidgetsHorizontal:
                    return ReflectionGuard.TryGetField(WidgetsSliderDraggingIdField, null, out id);
                case UiSliderKind.WidgetsRange:
                    return ReflectionGuard.TryGetField(WidgetsRangeDraggingIdField, null, out id);
                case UiSliderKind.GuiSlider:
                    return ReflectionGuard.TryGetField(GuiSliderDragIdField, null, out id);
                default:
                    return false;
            }
        }

        private static Rect AdjustHorizontalSliderRect(Rect rect, bool middleAlignment, string label)
        {
            if (middleAlignment || !string.IsNullOrEmpty(label))
            {
                rect.y += Mathf.Round((rect.height - 10f) / 2f);
            }
            if (!string.IsNullOrEmpty(label))
            {
                rect.y += 5f;
            }
            return rect;
        }

        private static bool IsTouchStartOverControlRect(Rect rect)
        {
            try
            {
                return rect.Contains(ScrollStartLocal());
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSliderAxisDrag(UiControlDragAxis axis, Vector2 dragFromStart)
        {
            float absX = Mathf.Abs(dragFromStart.x);
            float absY = Mathf.Abs(dragFromStart.y);
            bool pastCommit = dragFromStart.sqrMagnitude > DiagonalCommitDistancePixels * DiagonalCommitDistancePixels;
            if (axis == UiControlDragAxis.Horizontal)
            {
                return (absX > MovementDetectionSlopPixels && absX > absY * 1.5f)
                    || (pastCommit && absX >= absY);
            }
            if (axis == UiControlDragAxis.Vertical)
            {
                return (absY > MovementDetectionSlopPixels && absY > absX * 1.5f)
                    || (pastCommit && absY >= absX);
            }
            return false;
        }

        private static bool TryCommitSliderFromCurrentTouch(UiControlDragAxis axis, UiSliderKind kind,
            int controlId, ScrollProofEvent inputEvent)
        {
            if (uiScrollGestureActive || TouchSource.Count != 1)
            {
                return false;
            }
            Vector2 drag;
            try
            {
                drag = TouchToGui(TouchSource.GetTouch(0).position) - startGui;
            }
            catch
            {
                return false;
            }
            if (!IsSliderAxisDrag(axis, drag))
            {
                return false;
            }
            uiSliderState = UiSliderArbitrationState.Committed;
            uiSliderAxis = axis;
            uiSliderWindow = uiScrollCandidateWindow;
            uiSliderCapturedKind = kind;
            uiSliderCapturedId = controlId;
            RecordSliderArbitration("committed", kind, controlId, axis, drag, inputEvent);
            return true;
        }

        private static bool BeginSliderTouchCore(UiSliderKind kind, UiControlDragAxis axis, Rect hitRect,
            int explicitId, bool guardImmediateWrite, out SliderTouchState state)
        {
            state = default(SliderTouchState);
            Event current = Event.current;
            if (!TouchModeEnabled || current == null || TouchSource.Count != 1
                || uiScrollCandidateWindow == null || Find.WindowStack == null)
            {
                return false;
            }
            Window drawn = Find.WindowStack.currentlyDrawnWindow;
            if (drawn == null || drawn != uiScrollCandidateWindow)
            {
                return false;
            }

            state.Active = true;
            state.Kind = kind;
            state.Axis = axis;
            state.HitRect = hitRect;
            state.ExplicitId = explicitId;
            state.Window = drawn;
            state.CapturedKnown = TryReadSliderDragId(kind, out state.BeforeDragId);

            if (uiScrollGestureActive)
            {
                ClearActiveSliderDrags();
                if (current.type == EventType.MouseDrag || current.type == EventType.MouseDown)
                {
                    current.Use();
                }
                state.RestoreValue = guardImmediateWrite;
                return true;
            }

            if (uiSliderState == UiSliderArbitrationState.Committed && uiSliderWindow == drawn)
            {
                return true;
            }

            bool isTrackedSlider = false;
            if (uiSliderState == UiSliderArbitrationState.Pending && uiSliderWindow == drawn
                && uiSliderCapturedKind == kind)
            {
                if (explicitId != 0)
                {
                    isTrackedSlider = uiSliderCapturedId == explicitId;
                }
                else if (state.CapturedKnown && uiSliderCapturedId != 0)
                {
                    isTrackedSlider = state.BeforeDragId == uiSliderCapturedId;
                }
                else
                {
                    isTrackedSlider = IsTouchStartOverControlRect(hitRect);
                }
            }
            else if (uiSliderState == UiSliderArbitrationState.None)
            {
                bool alreadyCapturedHere = state.CapturedKnown && state.BeforeDragId != 0
                    && (explicitId == 0 || state.BeforeDragId == explicitId);
                if (alreadyCapturedHere || (current.type == EventType.MouseDrag && IsTouchStartOverControlRect(hitRect)))
                {
                    int capturedId = explicitId != 0 ? explicitId : (state.CapturedKnown ? state.BeforeDragId : 0);
                    uiSliderState = UiSliderArbitrationState.Pending;
                    uiSliderAxis = axis;
                    uiSliderWindow = drawn;
                    uiSliderCapturedKind = kind;
                    uiSliderCapturedId = capturedId;
                    isTrackedSlider = true;
                }
            }

            ScrollProofEvent inputEvent = TouchInputProof.IsRecording ? CaptureScrollProofEvent() : default(ScrollProofEvent);
            if (isTrackedSlider && (current.type == EventType.MouseDrag || guardImmediateWrite))
            {
                int idForCommit = explicitId != 0 ? explicitId : uiSliderCapturedId;
                if (TryCommitSliderFromCurrentTouch(axis, kind, idForCommit, inputEvent))
                {
                    return true;
                }
            }

            if (current.type == EventType.MouseDrag)
            {
                if (isTrackedSlider)
                {
                    // Freeze only the tracked slider's undecided drags so an earlier
                    // unrelated slider in the same window never eats this slider's
                    // MouseDrag event before it can commit.
                    current.Use();
                    state.RestoreValue = guardImmediateWrite;
                }
            }
            else if (guardImmediateWrite)
            {
                // FloatRange, IntRange, and Waylith GUI sliders mutate values on
                // MouseDown or on every Layout/Repaint pass while the button is held.
                // Allow native capture on MouseDown, then restore the value in Postfix
                // until this gesture commits to the slider.
                if (isTrackedSlider || (current.type == EventType.MouseDown && IsTouchStartOverControlRect(hitRect)))
                {
                    state.RestoreValue = true;
                }
            }

            return true;
        }

        private static void EndSliderTouchCore(ref SliderTouchState state)
        {
            if (!state.Active || uiScrollGestureActive || uiSliderState == UiSliderArbitrationState.Committed)
            {
                return;
            }

            int afterDragId;
            bool capturedNow = false;
            int capturedId = state.ExplicitId;
            if (TryReadSliderDragId(state.Kind, out afterDragId))
            {
                if (state.ExplicitId != 0)
                {
                    capturedNow = afterDragId == state.ExplicitId && state.BeforeDragId != state.ExplicitId;
                }
                else
                {
                    capturedNow = afterDragId != 0 && afterDragId != state.BeforeDragId;
                    capturedId = afterDragId;
                }
            }
            else if (!state.CapturedKnown && uiSliderState == UiSliderArbitrationState.None
                && IsTouchStartOverControlRect(state.HitRect))
            {
                capturedNow = true;
                capturedId = state.ExplicitId;
            }

            if (capturedNow)
            {
                uiSliderState = UiSliderArbitrationState.Pending;
                uiSliderAxis = state.Axis;
                uiSliderWindow = state.Window;
                uiSliderCapturedKind = state.Kind;
                uiSliderCapturedId = capturedId;
                if (state.Kind != UiSliderKind.WidgetsHorizontal)
                {
                    state.RestoreValue = true;
                }
                if (TouchInputProof.IsRecording)
                {
                    Vector2 drag = Vector2.zero;
                    try { if (TouchSource.Count > 0) drag = TouchToGui(TouchSource.GetTouch(0).position) - startGui; } catch { }
                    RecordSliderArbitration("pending", state.Kind, capturedId, state.Axis, drag, CaptureScrollProofEvent());
                }
            }
        }

        public static void BeginHorizontalSliderTouch(Rect rect, bool middleAlignment, string label,
            out SliderTouchState state)
        {
            Rect hitRect = AdjustHorizontalSliderRect(rect, middleAlignment, label);
            BeginSliderTouchCore(UiSliderKind.WidgetsHorizontal, UiControlDragAxis.Horizontal,
                hitRect, 0, false, out state);
        }

        public static void EndHorizontalSliderTouch(SliderTouchState state)
        {
            EndSliderTouchCore(ref state);
        }

        public static void BeginFloatRangeTouch(Rect rect, int id, FloatRange range, out FloatRangeTouchState state)
        {
            state = new FloatRangeTouchState { OriginalValue = range };
            BeginSliderTouchCore(UiSliderKind.WidgetsRange, UiControlDragAxis.Horizontal,
                rect, id, true, out state.Slider);
        }

        public static void EndFloatRangeTouch(FloatRangeTouchState state, ref FloatRange range)
        {
            EndSliderTouchCore(ref state.Slider);
            if (state.Slider.RestoreValue)
            {
                range = state.OriginalValue;
            }
        }

        public static void BeginIntRangeTouch(Rect rect, int id, IntRange range, out IntRangeTouchState state)
        {
            state = new IntRangeTouchState { OriginalValue = range };
            BeginSliderTouchCore(UiSliderKind.WidgetsRange, UiControlDragAxis.Horizontal,
                rect, id, true, out state.Slider);
        }

        public static void EndIntRangeTouch(IntRangeTouchState state, ref IntRange range)
        {
            EndSliderTouchCore(ref state.Slider);
            if (state.Slider.RestoreValue)
            {
                range = state.OriginalValue;
            }
        }

        public static void BeginGuiSliderTouch(Rect position, float value, bool horizontal, out GuiSliderTouchState state)
        {
            state = new GuiSliderTouchState { OriginalValue = value };
            BeginSliderTouchCore(UiSliderKind.GuiSlider,
                horizontal ? UiControlDragAxis.Horizontal : UiControlDragAxis.Vertical,
                position, 0, true, out state.Slider);
        }

        public static void EndGuiSliderTouch(GuiSliderTouchState state, ref float result)
        {
            EndSliderTouchCore(ref state.Slider);
            if (state.Slider.RestoreValue)
            {
                result = state.OriginalValue;
            }
        }

        public static void FilterHorizontalSliderDrag()
        {
            Event current = Event.current;
            if (!TouchModeEnabled || current == null || current.type != EventType.MouseDrag
                || TouchSource.Count != 1 || uiScrollCandidateWindow == null
                || Find.WindowStack == null || uiScrollCandidateWindow != Find.WindowStack.currentlyDrawnWindow) return;
            if (uiScrollGestureActive)
            {
                current.Use();
                return;
            }
            if (uiSliderState == UiSliderArbitrationState.Committed && uiSliderWindow == uiScrollCandidateWindow)
            {
                return;
            }
            ScrollProofEvent inputEvent = TouchInputProof.IsRecording ? CaptureScrollProofEvent() : default(ScrollProofEvent);
            if (!TryCommitSliderFromCurrentTouch(UiControlDragAxis.Horizontal, UiSliderKind.WidgetsHorizontal,
                uiSliderCapturedId, inputEvent))
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
                if (uiSliderState == UiSliderArbitrationState.Committed && uiSliderWindow == drawn)
                {
                    decision = ScrollDecision.ControlLocked;
                    return false;
                }

                float effectiveMaxX = maxX;
                float effectiveMaxY = maxY;
                ApplyPendingSliderAxisMask(drawn, ref effectiveMaxX, ref effectiveMaxY);

                // Distance alone must never steal a horizontal slider drag.
                Vector2 dragFromStart = gui - startGui;
                bool directionClaim = IsScrollDirectionDrag(effectiveMaxX, effectiveMaxY, dragFromStart);
                decision = ScrollDecision.BelowSlop;
                if (!directionClaim)
                {
                    return false;
                }

                if (!HasScrollHeadroom(effectiveMaxX, effectiveMaxY, scrollPosition, dragFromStart)
                    || DeferToInnerScrollView(drawn, outRect, dragFromStart))
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

        private static void ApplyPendingSliderAxisMask(Window drawn, ref float maxX, ref float maxY)
        {
            if (uiSliderState != UiSliderArbitrationState.Pending || uiSliderWindow != drawn)
            {
                return;
            }
            if (uiSliderAxis == UiControlDragAxis.Horizontal)
            {
                maxX = 0f;
            }
            else if (uiSliderAxis == UiControlDragAxis.Vertical)
            {
                maxY = 0f;
            }
        }

        private static bool DeferToInnerScrollView(Window drawn, Rect outRect, Vector2 dragFromStart)
        {
            float area = outRect.width * outRect.height;
            float candidateMaxX = uiScrollCandidateMaximum.x;
            float candidateMaxY = uiScrollCandidateMaximum.y;
            ApplyPendingSliderAxisMask(drawn, ref candidateMaxX, ref candidateMaxY);
            if (uiScrollCandidateArea >= area - 1f
                || !IsScrollDirectionDrag(candidateMaxX, candidateMaxY, dragFromStart)
                || !HasScrollHeadroom(candidateMaxX, candidateMaxY,
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
        // Between slop and 2*slop, the dominant component must exceed 1.5x the
        // other; past 2*slop, diagonal drags resolve by the larger component
        // (strictly disjoint from IsSliderAxisDrag's >= tie-break).
        private static bool IsScrollDirectionDrag(float maxX, float maxY, Vector2 dragFromStart)
        {
            float absX = Mathf.Abs(dragFromStart.x);
            float absY = Mathf.Abs(dragFromStart.y);
            bool pastCommit = dragFromStart.sqrMagnitude > DiagonalCommitDistancePixels * DiagonalCommitDistancePixels;
            if (maxY > 1f && ((absY > MovementDetectionSlopPixels && absY > absX * 1.5f)
                || (pastCommit && absY > absX)))
            {
                return true;
            }
            if (maxX > 1f && ((absX > MovementDetectionSlopPixels && absX > absY * 1.5f)
                || (pastCommit && absX > absY)))
            {
                return true;
            }
            return false;
        }

        private static void ClaimTouchForUiScroll()
        {
            if (uiSliderState != UiSliderArbitrationState.None && TouchInputProof.IsRecording)
            {
                Vector2 drag = Vector2.zero;
                try { if (TouchSource.Count > 0) drag = TouchToGui(TouchSource.GetTouch(0).position) - startGui; } catch { }
                RecordSliderArbitration("clearedByScroll", uiSliderCapturedKind, uiSliderCapturedId,
                    uiSliderAxis, drag, CaptureScrollProofEvent());
            }
            ResetUiSliderArbitration();
            ClearActiveSliderDrags();

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
