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
        private const float DragSlopPixels = 18f;
        private const float MovementDetectionSlopPixels = 6f;
        private const int TouchDropoutGraceFrames = 2;
        private const float SelectHoldSeconds = 0.32f;
        private const float RightClickHoldSeconds = 0.50f;
        private const float RightClickHoldSlopPixels = 22f;
        private const float TouchMapTargetRadiusPixels = 28f;
        private const float FallbackMinZoomSize = 11f;
        private const float FallbackMaxZoomSize = 60f;
        private const float DefaultPanInertiaStrength = 1.05f;
        private const float DefaultPanInertiaDamping = 4.8f;
        private const float MinPanInertiaStartSpeed = 0.12f;
        private const float MinPanInertiaSpeed = 0.035f;
        private const float MaxPanInertiaSpeed = 120f;
        private const float PanVelocityHistorySeconds = 0.14f;
        private const int PanVelocitySampleCapacity = 6;
        private const float WorldPanPixelsToRotation = 0.12f;
        private const float WorldPanInertiaStrengthScale = 0.56f;
        private const float MinWorldPanInertiaStartSpeed = 80f;
        private const float MinWorldPanInertiaSpeed = 8f;
        private const float MaxWorldPanInertiaSpeed = 3500f;
        private const float WorldPanVelocityHistorySeconds = 0.14f;
        private const int WorldPanVelocitySampleCapacity = 6;
        private const float WorldAnchorPoleDampingStart = 0.74f;
        private const float WorldAnchorPoleMinimumScale = 0.18f;
        private const float MaxWorldAnchorRotationDegrees = 6f;
        private const float WorldPoleInertiaDisable = 0.92f;
        private const float WorldCloseZoomInertiaStartSpeed = 100f;
        private const float WorldCloseZoomInertiaStopSpeed = 55f;
        private const float WorldCloseZoomInertiaDampingScale = 2.15f;
        private const float WorldCloseZoomInertiaMoveScale = 0.62f;
        private const float TwoFingerCancelTapSeconds = 0.42f;
        private const float TwoFingerCancelMoveSlopPixels = 18f;
        private const float TwoFingerCancelDistanceSlopPixels = 18f;
        private const float DefaultZoomInertiaStrength = 1.05f;
        private const float DefaultZoomInertiaDamping = 4.8f;
        private const float MinZoomInertiaStartSpeed = 0.25f;
        private const float MinZoomInertiaSpeed = 0.06f;
        private const float MaxZoomInertiaSpeed = 8f;
        private const int ZoomVelocitySampleCapacity = 6;
        private const float ZoomVelocityHistorySeconds = 0.14f;
        private const float MinUiScrollInertiaStartSpeed = 120f;
        private const float MinUiScrollInertiaSpeed = 8f;
        private const float MaxUiScrollInertiaSpeed = 5000f;
        private const int UiScrollVelocitySampleCapacity = 5;
        private const float UiScrollVelocityHistorySeconds = 0.12f;

        private static readonly FieldInfo CameraRootPosField = ReflectionGuard.Field(typeof(CameraDriver), "rootPos");
        private static readonly FieldInfo CameraRootSizeField = ReflectionGuard.Field(typeof(CameraDriver), "rootSize");
        private static readonly FieldInfo CameraVelocityField = ReflectionGuard.Field(typeof(CameraDriver), "velocity");
        private static readonly FieldInfo CameraDesiredDollyField = ReflectionGuard.Field(typeof(CameraDriver), "desiredDolly");
        private static readonly FieldInfo CameraDesiredDollyRawField = ReflectionGuard.Field(typeof(CameraDriver), "desiredDollyRaw");
        private static readonly FieldInfo CameraMouseBottomEdgeStartField = ReflectionGuard.Field(typeof(CameraDriver), "mouseTouchingScreenBottomEdgeStartTime");
        private static readonly FieldInfo SelectorDragBoxField = ReflectionGuard.Field(typeof(Selector), "dragBox");
        private static readonly FieldInfo DragBoxActiveField = ReflectionGuard.Field(typeof(DragBox), "active");
        private static readonly FieldInfo DragBoxStartField = ReflectionGuard.Field(typeof(DragBox), "start");
        private static readonly FieldInfo FloatMenuOptionActionField = ReflectionGuard.Field(typeof(FloatMenuOption), "action");
        private static readonly MethodInfo SelectorSelectInsideDragBoxMethod = ReflectionGuard.Method(typeof(Selector), "SelectInsideDragBox");
        private static readonly MethodInfo FloatMenuMakerMapGetAutoTakeOptionMethod = ReflectionGuard.Method(typeof(FloatMenuMakerMap), "GetAutoTakeOption");
        private static readonly FieldInfo WorldCameraRotationVelocityField = ReflectionGuard.Field(typeof(WorldCameraDriver), "rotationVelocity");
        private static readonly FieldInfo WorldCameraMouseBottomEdgeStartField = ReflectionGuard.Field(typeof(WorldCameraDriver), "mouseTouchingScreenBottomEdgeStartTime");
        private static readonly FieldInfo WorldCameraAltitudeField = ReflectionGuard.Field(typeof(WorldCameraDriver), "altitude");
        private static readonly FieldInfo WorldCameraDesiredAltitudeField = ReflectionGuard.Field(typeof(WorldCameraDriver), "desiredAltitude");
        private static readonly FieldInfo WorldCameraMaxAltitudeField = ReflectionGuard.Field(typeof(WorldCameraDriver), "MaxAltitude", false);
        private static readonly FieldInfo WorldCameraSphereRotationField = ReflectionGuard.Field(typeof(WorldCameraDriver), "sphereRotation");
        private static readonly FieldInfo WorldRotationAnimationLerpField = ReflectionGuard.Field(typeof(WorldCameraDriver), "rotationAnimation_lerpFactor", false);
        private static readonly FieldInfo WorldCameraSphereRadiusField = ReflectionGuard.Field(typeof(WorldCameraDriver), "SphereRadius", false);
        private static readonly FieldInfo WorldCameraLayerOriginOffsetField = ReflectionGuard.Field(typeof(WorldCameraDriver), "layerOriginOffset", false);
        private static readonly FieldInfo WorldCameraCachedCameraField = ReflectionGuard.Field(typeof(WorldCameraDriver), "cachedCamera");
        private static readonly MethodInfo WorldCameraApplyPositionMethod = ReflectionGuard.Method(typeof(WorldCameraDriver), "ApplyPositionToGameObject");
        private static readonly FieldInfo WorldSelectorDragBoxField = ReflectionGuard.Field(typeof(WorldSelector), "dragBox");
        private static readonly FieldInfo WorldDragBoxActiveField = ReflectionGuard.Field(typeof(WorldDragBox), "active");
        private static readonly FieldInfo WorldDragBoxStartField = ReflectionGuard.Field(typeof(WorldDragBox), "start");
        private static readonly MethodInfo WorldSelectorSelectInsideDragBoxMethod = ReflectionGuard.Method(typeof(WorldSelector), "SelectInsideDragBox");
        private static readonly PropertyInfo WorldRenderedNowProperty = ReflectionGuard.Property(typeof(WorldRendererUtility), "WorldRenderedNow", false);
        private static readonly MethodInfo WorldRenderedNowGetter = ReflectionGuard.Method(typeof(WorldRendererUtility), "get_WorldRenderedNow", false);
        private static readonly FieldInfo WorldRenderedNowField = ReflectionGuard.Field(typeof(WorldRendererUtility), "WorldRenderedNow", false);

        private static TouchMode mode = TouchMode.None;
        private static long primaryFingerId = -1;
        private static Vector2 startGui;
        private static Vector2 lastGui;
        private static Vector3 startMap;
        private static Vector3 panAnchorMap;
        private static Vector3 twoFingerPanAnchorMap;
        private static float startTime;
        private static bool startedOverUi;
        private static bool rightClickDone;
        private static float lastWorldTapTime = -100f;
        private static Vector2 lastWorldTapGui = Vector2.zero;
        private static bool worldDoubleTapPrimed;
        private static Vector2 worldDragBoxStartGui = Vector2.zero;
        private static bool suppressReleaseTap;
        private static bool worldPanAnchorValid;
        private static Vector3 worldPanAnchorPoint;
        // The same grabbed point in sphere-LOCAL coords (a fixed geographic location).
        // The projected turntable needs it: each frame it uses the anchor CURRENT world
        // direction (sphereRotation * local), not the drag-start world direction, so the
        // projection is a feedback loop instead of accumulating error.
        private static Vector3 worldPanAnchorLocal = Vector3.zero;
        private static bool worldInertiaAnchorValid;
        private static Vector3 worldInertiaAnchorPoint;
        private static Vector2 worldInertiaGui;

        private static float lastTwoFingerDistance;
        private static Vector2 lastTwoFingerMidpoint;
        private static readonly TwistGesture GlobeTwist = new TwistGesture();
        private static Vector2 twoFingerStartGuiA;
        private static Vector2 twoFingerStartGuiB;
        private static float twoFingerStartDistance;
        private static Vector2 twoFingerStartMidpoint;
        private static float twoFingerStartTime;
        private static float twoFingerMaxMove;
        private static float twoFingerMaxDistanceChange;
        private static bool twoFingerStartedWithVanillaMapTool;
        private static int lastTouchFrame = -1000;
        private static int lastMultiTouchFrame = -1000;
        private static int suppressUiClickUntilFrame = -1000;
        private static int suppressWindowCloseUntilFrame = -1000;
        private static int suppressVanillaMapInputUntilFrame = -1000;
        private static bool ignoreTouchesUntilAllReleased;
        private static bool hadTouchLastFrame;
        private static bool touchFailureLogged;
        private static string lastBackendStatus;
        private static int touchDropoutFrames;
        private static bool suppressEdgeScrollUntilMouseMoves;
        private static bool suppressVanillaMapUntilMouseMoves;
        private static bool uiScrollGestureActive;
        private static Rect uiScrollActiveRect;
        private static Vector2 uiScrollLastGui;
        private static int lastUiScrollApplyFrame = -1000;
        private static Vector2 uiScrollVelocity;
        private static bool uiScrollInertiaActive;
        private static Rect uiScrollInertiaRect;
        private static float lastUiScrollSampleTime;
        private static float lastUiScrollInertiaTime;
        private static float uiScrollInertiaStartTime;
        private static int lastUiScrollInertiaApplyFrame = -1000;
        private static Vector3 mousePositionWhenTouchEnded;
        private static Vector3 panVelocity;
        private static Vector2 worldPanVelocity;
        private static Vector2 pendingWorldCameraDolly;
        private static Vector2 zoomInertiaGui;
        private static bool zoomInertiaAnchorValid;
        private static float zoomVelocity;
        private static float lastUpdateTime;
        private static float lastPanSampleTime;
        private static float lastWorldPanSampleTime;
        private static float lastZoomSampleTime;
        private static float panGestureDistance;
        private static float lastSignificantMovementElapsed;
        private static bool selectPrimed;
        private static readonly Vector3[] PanVelocitySamples = new Vector3[PanVelocitySampleCapacity];
        private static readonly float[] PanVelocitySampleTimes = new float[PanVelocitySampleCapacity];
        private static int panVelocitySampleIndex;
        private static int panVelocitySampleCount;
        private static readonly float[] ZoomVelocitySamples = new float[ZoomVelocitySampleCapacity];
        private static readonly float[] ZoomVelocitySampleTimes = new float[ZoomVelocitySampleCapacity];
        private static int zoomVelocitySampleIndex;
        private static int zoomVelocitySampleCount;
        private static readonly Vector2[] WorldPanVelocitySamples = new Vector2[WorldPanVelocitySampleCapacity];
        private static readonly float[] WorldPanVelocitySampleTimes = new float[WorldPanVelocitySampleCapacity];
        private static int worldPanVelocitySampleIndex;
        private static int worldPanVelocitySampleCount;
        private static readonly Vector2[] UiScrollVelocitySamples = new Vector2[UiScrollVelocitySampleCapacity];
        private static readonly float[] UiScrollVelocitySampleTimes = new float[UiScrollVelocitySampleCapacity];
        private static int uiScrollVelocitySampleIndex;
        private static int uiScrollVelocitySampleCount;
        private static readonly Vector2[] MapTargetSearchOffsets = new Vector2[]
        {
            Vector2.zero,
            new Vector2(0f, -TouchMapTargetRadiusPixels),
            new Vector2(TouchMapTargetRadiusPixels, 0f),
            new Vector2(0f, TouchMapTargetRadiusPixels),
            new Vector2(-TouchMapTargetRadiusPixels, 0f),
            new Vector2(TouchMapTargetRadiusPixels, -TouchMapTargetRadiusPixels),
            new Vector2(TouchMapTargetRadiusPixels, TouchMapTargetRadiusPixels),
            new Vector2(-TouchMapTargetRadiusPixels, TouchMapTargetRadiusPixels),
            new Vector2(-TouchMapTargetRadiusPixels, -TouchMapTargetRadiusPixels)
        };

        private static bool TouchModeEnabled
        {
            get
            {
                return RimTouchMod.Settings != null && RimTouchMod.Settings.enableTouchMode;
            }
        }

        public static bool RecentTouchInput
        {
            get
            {
                return TouchModeEnabled
                    && Time.frameCount - lastTouchFrame <= 30;
            }
        }

        public static bool ShouldSuppressUiClickFromGesture
        {
            get
            {
                return TouchModeEnabled
                    && (TouchSource.Count >= 2
                        || uiScrollGestureActive
                        || Time.frameCount <= suppressUiClickUntilFrame
                        || Time.frameCount - lastMultiTouchFrame <= 30);
            }
        }

        public static bool ShouldSuppressWindowCloseFromGesture
        {
            get
            {
                return TouchModeEnabled
                    && (TouchSource.Count >= 2
                        || uiScrollGestureActive
                        || HasCurrentOneFingerLocalMapTouch()
                        || Time.frameCount <= suppressWindowCloseUntilFrame
                        || Time.frameCount - lastMultiTouchFrame <= 30);
            }
        }

        public static bool ShouldSuppressMainTabsFromGesture
        {
            get
            {
                return ShouldSuppressUiClickFromGesture || ShouldSuppressWindowCloseFromGesture;
            }
        }

        public static bool ShouldSuppressUiClickInRect(Rect rect)
        {
            if (!ShouldSuppressUiClickFromGesture)
            {
                return false;
            }

            Event current = Event.current;
            if (current != null && current.isMouse)
            {
                return rect.Contains(current.mousePosition);
            }

            try
            {
                int count = Math.Min(TouchSource.Count, 2);
                for (int i = 0; i < count; i++)
                {
                    Vector2 gui = TouchToGui(TouchSource.GetTouch(i).position);
                    if (rect.Contains(GuiToLocalGuiPoint(gui)) || rect.Contains(gui))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public static bool ShouldSuppressAfterMultiTouch
        {
            get
            {
                return TouchModeEnabled
                    && Time.frameCount - lastMultiTouchFrame <= 30;
            }
        }

        public static bool ShouldSuppressEdgeScroll
        {
            get
            {
                return TouchModeEnabled
                    && TouchSource.Count == 0
                    && (Time.frameCount - lastTouchFrame <= 90 || suppressEdgeScrollUntilMouseMoves)
                    && !IsVanillaMapToolActive();
            }
        }

        public static bool ShouldSuppressVanillaMapInput
        {
            get
            {
                if (!TouchModeEnabled)
                {
                    return false;
                }

                if (TouchSource.Count >= 2)
                {
                    return true;
                }

                if (Time.frameCount <= suppressVanillaMapInputUntilFrame)
                {
                    return true;
                }

                if (ShouldSuppressAfterMultiTouch)
                {
                    return true;
                }

                if (TouchSource.Count > 0 && Find.CurrentMap != null && !IsVanillaMapToolActive())
                {
                    Vector2 gui = TouchToGui(TouchSource.GetTouch(0).position);
                    if (!IsLikelyUiPosition(gui))
                    {
                        return true;
                    }
                }

                if (!startedOverUi && Time.frameCount - lastTouchFrame <= 45 && !IsVanillaMapToolActive())
                {
                    return true;
                }

                if (suppressVanillaMapUntilMouseMoves && !IsVanillaMapToolActive())
                {
                    return true;
                }

                return mode != TouchMode.None
                    && mode != TouchMode.VanillaMapTool
                    && (!startedOverUi || mode == TouchMode.TwoFinger);
            }
        }

        public static bool ShouldSuppressVanillaMapToolInput
        {
            get
            {
                if (!TouchModeEnabled || Find.CurrentMap == null)
                {
                    return false;
                }

                if (TouchSource.Count >= 2)
                {
                    return true;
                }

                return mode == TouchMode.TwoFinger
                    || ignoreTouchesUntilAllReleased
                    || Time.frameCount - lastMultiTouchFrame <= 30;
            }
        }

        public static bool ShouldSuppressVanillaWorldInput
        {
            get
            {
                if (!TouchModeEnabled || !IsWorldMapActive())
                {
                    return false;
                }

                if (TouchSource.Count >= 2)
                {
                    return true;
                }

                if (TouchSource.Count == 1)
                {
                    Vector2 gui = TouchToGui(TouchSource.GetTouch(0).position);
                    return !IsLikelyUiPosition(gui);
                }

                return Time.frameCount - lastTouchFrame <= 45 || suppressVanillaMapUntilMouseMoves;
            }
        }

        public static bool ShouldSuppressVanillaDragBoxDrawing
        {
            get
            {
                return ShouldSuppressVanillaMapInput && mode != TouchMode.SelectionHold;
            }
        }

        public static bool ShouldSuppressVanillaWorldDragBoxDrawing
        {
            get
            {
                return ShouldSuppressVanillaWorldInput;
            }
        }

        public static bool ShouldSuppressWorldEdgeScroll
        {
            get
            {
                return TouchModeEnabled
                    && IsWorldMapActive()
                    && (TouchSource.Count > 0 || Time.frameCount - lastTouchFrame <= 90 || suppressEdgeScrollUntilMouseMoves);
            }
        }

        public static bool ShouldSuppressWorldHover
        {
            get
            {
                return TouchModeEnabled
                    && IsWorldMapActive()
                    && (TouchSource.Count >= 2
                        || mode == TouchMode.TwoFinger
                        || Time.frameCount - lastMultiTouchFrame <= 15);
            }
        }

        public static void Update()
        {
            try
            {
                TouchSource.Update(TouchModeEnabled);
                if (lastBackendStatus != TouchSource.Status)
                {
                    lastBackendStatus = TouchSource.Status;
                    Log.Message("[RimTouch] " + lastBackendStatus);
                }
                if (!TouchModeEnabled || TouchSource.Canceled)
                {
                    if (TouchSource.Canceled || hadTouchLastFrame || mode != TouchMode.None)
                    {
                        AbortTouchInput();
                    }
                    return;
                }

                int touchCount = TouchSource.Count;
                if (touchCount <= 0)
                {
                    if (hadTouchLastFrame)
                    {
                        touchDropoutFrames++;
                        if (touchDropoutFrames <= TouchDropoutGraceFrames)
                        {
                            // A single (or occasionally two) consecutive frame(s) reporting zero
                            // touches even though the finger never actually left the screen is a
                            // known quirk of Windows/Unity legacy touch reporting, especially under
                            // frame-rate dips. Treating that as a real lift used to tear down and
                            // silently restart the whole gesture mid-drag at a random point - which
                            // re-triggered the Pan/SelectionHold classification from scratch and
                            // could misfire into a selection box for no visible reason. Ride out the
                            // gap instead and keep the existing gesture state intact.
                            UpdateInertia();
                            return;
                        }
                    }

                    bool finalizedTouch = false;
                    if (hadTouchLastFrame)
                    {
                        FinalizeTouchDisappearance();
                        finalizedTouch = true;
                        suppressEdgeScrollUntilMouseMoves = true;
                        if (!startedOverUi && mode != TouchMode.VanillaMapTool)
                        {
                            suppressVanillaMapUntilMouseMoves = true;
                        }
                        mousePositionWhenTouchEnded = Input.mousePosition;
                    }
                    hadTouchLastFrame = false;
                    touchDropoutFrames = 0;
                    UpdateEdgeScrollSuppression();
                    ignoreTouchesUntilAllReleased = false;
                    if (mode != TouchMode.None)
                    {
                        if (!finalizedTouch)
                        {
                            FinalizeInertiaFromGesture();
                        }
                        ResetState();
                    }
                    if (ShouldSuppressVanillaMapInput)
                    {
                        CancelRimWorldDragBox();
                    }
                    if (ShouldSuppressVanillaWorldInput)
                    {
                        CancelWorldDragBox();
                    }
                    UpdateInertia();
                    return;
                }

                touchDropoutFrames = 0;

                if (!hadTouchLastFrame)
                {
                    StopInertia();
                    StopUiScrollInertia();
                    ClearInertiaSamples();
                }

                hadTouchLastFrame = true;
                suppressEdgeScrollUntilMouseMoves = true;
                mousePositionWhenTouchEnded = Input.mousePosition;
                lastTouchFrame = Time.frameCount;
                lastUpdateTime = Time.realtimeSinceStartup;

                if (touchCount >= 2)
                {
                    lastMultiTouchFrame = Time.frameCount;
                    ignoreTouchesUntilAllReleased = true;
                    UpdateTwoFinger(TouchSource.GetTouch(0), TouchSource.GetTouch(1));
                    return;
                }

                if (ignoreTouchesUntilAllReleased)
                {
                    CancelRimWorldDragBox();
                    return;
                }

                ContactTouch touch = TouchSource.GetTouch(0);
                UpdateOneFinger(touch);
            }
            catch (Exception ex)
            {
                if (!touchFailureLogged)
                {
                    touchFailureLogged = true;
                    Log.Warning("[RimTouch] Touch update failed; further duplicate failures are suppressed: " + ex);
                }
                AbortTouchInput();
            }
        }

        private static void AbortTouchInput()
        {
            // Cancellation must not flow through FinishTouch or disappearance
            // finalization: those can select, order, or queue a UI tap.
            StopInertia();
            StopUiScrollInertia();
            ClearInertiaSamples();
            ResetState();
            TouchTapRepair.Clear();
            hadTouchLastFrame = false;
            ignoreTouchesUntilAllReleased = false;
            touchDropoutFrames = 0;
            lastTouchFrame = lastMultiTouchFrame = -1000;
            suppressUiClickUntilFrame = suppressWindowCloseUntilFrame = -1000;
            suppressVanillaMapInputUntilFrame = -1000;
            suppressEdgeScrollUntilMouseMoves = suppressVanillaMapUntilMouseMoves = false;
        }

        public static void OnGUI()
        {
            EnsureRimWorldDragBoxState();
            if (mode == TouchMode.WorldDragBox && IsWorldMapActive())
            {
                Rect box = new Rect(
                    Mathf.Min(worldDragBoxStartGui.x, lastGui.x),
                    Mathf.Min(worldDragBoxStartGui.y, lastGui.y),
                    Mathf.Abs(lastGui.x - worldDragBoxStartGui.x),
                    Mathf.Abs(lastGui.y - worldDragBoxStartGui.y));
                DrawMarchingAntsRect(box);
            }
        }

        private static void UpdateOneFinger(ContactTouch touch)
        {
            Vector2 gui = TouchToGui(touch.position);

            if (touch.phase == ContactPhase.Began || mode == TouchMode.None || primaryFingerId != touch.fingerId)
            {
                primaryFingerId = touch.fingerId;
                startGui = gui;
                lastGui = gui;
                startMap = IsWorldMapActive() ? Vector3.zero : (Find.CurrentMap != null ? GuiToMapPosition(gui) : Vector3.zero);
                startTime = Time.realtimeSinceStartup;
                lastWorldPanSampleTime = startTime;
                startedOverUi = IsLikelyUiPosition(gui);
                rightClickDone = false;
                suppressReleaseTap = false;
                panGestureDistance = 0f;
                lastSignificantMovementElapsed = 0f;
                selectPrimed = false;
                ClearInertiaSamples();
                // Point-and-drag (world): double-tap, hold, drag to multi-select. The
                // standard double-tap window is 300 ms; the second tap must land near
                // the first so a tap elsewhere does not arm it. This only arms the
                // gesture; the drag-box itself starts once the finger moves past slop.
                worldDoubleTapPrimed = false;
                if (!startedOverUi && IsWorldMapActive() && WorldPointDragEnabled())
                {
                    float sinceLastTap = Time.realtimeSinceStartup - lastWorldTapTime;
                    if (sinceLastTap <= 0.3f && Vector2.Distance(gui, lastWorldTapGui) <= 40f)
                    {
                        worldDoubleTapPrimed = true;
                    }
                }
                if (!startedOverUi)
                {
                    SuppressUiClickFallout();
                }
                if (!startedOverUi && IsVanillaMapToolActive())
                {
                    mode = TouchMode.VanillaMapTool;
                    return;
                }
                if (!startedOverUi && Find.CurrentMap != null && !IsWorldMapActive())
                {
                    SuppressVanillaMapInputFallout();
                    CancelRimWorldDragBox();
                }
                mode = TouchMode.OneFinger;
                return;
            }

            float elapsed = Time.realtimeSinceStartup - startTime;
            float moveDistance = Vector2.Distance(gui, startGui);

            if (mode == TouchMode.VanillaMapTool)
            {
                lastGui = gui;
                if (IsEnded(touch))
                {
                    FinishTouch();
                }
                return;
            }

            if (startedOverUi)
            {
                if (IsEnded(touch) && moveDistance <= DragSlopPixels && !suppressReleaseTap)
                {
                    QueueUiTap(gui);
                }
                lastGui = gui;
                if (IsEnded(touch))
                {
                    FinalizeUiScrollInertia();
                    FinishTouch();
                }
                return;
            }

            if (IsWorldMapActive())
            {
                UpdateOneFingerWorld(touch, gui);
                return;
            }

            if (mode == TouchMode.OneFinger)
            {
                SuppressVanillaMapInputFallout();
                CancelRimWorldDragBox();

                float frameDelta = Vector2.Distance(gui, lastGui);
                if (frameDelta > MovementDetectionSlopPixels)
                {
                    // The finger is actively, continuously moving right now - keep pushing back
                    // the "last significant movement" timestamp so a slow but steady drag never
                    // accumulates enough stationary time to be mistaken for a long-press-hold.
                    lastSignificantMovementElapsed = elapsed;
                }

                float stationaryDuration = elapsed - lastSignificantMovementElapsed;
                if (!selectPrimed && stationaryDuration >= SelectHoldSeconds && moveDistance <= RightClickHoldSlopPixels)
                {
                    // The finger has genuinely sat still (no meaningful per-frame movement) for
                    // long enough, without having already wandered past the slop. Only now do we
                    // treat a subsequent drag as an intentional "hold-then-select" gesture instead
                    // of a pan - matching "hold WITHOUT movement -> SelectionHold" literally,
                    // regardless of how fast or slow the eventual drag itself turns out to be.
                    selectPrimed = true;
                }

                if (moveDistance > DragSlopPixels && !selectPrimed)
                {
                    mode = TouchMode.MapPan;
                    panAnchorMap = startMap;
                    panGestureDistance = moveDistance;
                    lastPanSampleTime = startTime;
                    suppressReleaseTap = true;
                    SuppressUiClickFallout();
                    CancelRimWorldDragBox();
                }
                else if (elapsed >= RightClickHoldSeconds && moveDistance <= RightClickHoldSlopPixels && !rightClickDone)
                {
                    DoMapRightClick(gui);
                    rightClickDone = true;
                    suppressReleaseTap = true;
                    CancelRimWorldDragBox();
                }
                else if (moveDistance > RightClickHoldSlopPixels && selectPrimed)
                {
                    mode = TouchMode.SelectionHold;
                    suppressReleaseTap = true;
                    BeginRimWorldDragBox();
                }
            }

            if (mode == TouchMode.MapPan)
            {
                SuppressUiClickFallout();
                SuppressVanillaMapInputFallout();
                panGestureDistance = Mathf.Max(panGestureDistance, moveDistance);
                Vector3 appliedDelta = KeepMapPointUnderGui(panAnchorMap, gui);
                if (moveDistance > DragSlopPixels)
                {
                    SamplePanVelocityFromMapDelta(appliedDelta);
                }
                CancelRimWorldDragBox();
            }

            if (IsEnded(touch))
            {
                if (mode == TouchMode.OneFinger && moveDistance <= DragSlopPixels && !rightClickDone)
                {
                    if (!TryDoDraftedPawnTapCommand(gui))
                    {
                        DoMapLeftClick(gui);
                    }
                }
                else if (mode == TouchMode.SelectionHold)
                {
                    FinishRimWorldDragBox();
                }
                FinalizeInertiaFromGesture();
                FinishTouch();
                return;
            }

            lastGui = gui;
        }

        private static void UpdateOneFingerWorld(ContactTouch touch, Vector2 gui)
        {
            float moveDistance = Vector2.Distance(gui, startGui);

            if (mode == TouchMode.OneFinger)
            {
                // No time window here: the old `elapsed < SelectHoldSeconds` gate
                // meant a slow drag (one that took longer than 0.32 s to travel
                // 18 px) could never become a pan at all, so the globe simply
                // ignored it. The colony-map path already classifies purely on
                // distance for the same reason; tap vs. pan on the world is
                // still decided at release by the slop check below.
                if (moveDistance > DragSlopPixels)
                {
                    if (worldDoubleTapPrimed)
                    {
                        // Point-and-drag: double-tap, hold, drag. Enters the world
                        // drag-box instead of panning, so the gesture is deliberate
                        // and reliable (no more "sometimes it multi-selects, sometimes
                        // it pans" from the touch leaking through to vanilla).
                        mode = TouchMode.WorldDragBox;
                        panGestureDistance = moveDistance;
                        suppressReleaseTap = true;
                        SuppressUiClickFallout();
                        BeginWorldDragBox(startGui);
                    }
                    else
                    {
                        mode = TouchMode.MapPan;
                        panGestureDistance = moveDistance;
                        lastWorldPanSampleTime = startTime;
                        worldPanAnchorValid = ShouldUseWorldAnchorPan() && TryGetWorldSpherePointAtGui(startGui, out worldPanAnchorPoint);
                        if (worldPanAnchorValid) UpdateWorldPanAnchorLocal();
                        if (worldPanAnchorValid && LockNorthUpOnDragEnabled() && AutoLockNorthOnDragEnabled())
                        {
                            SnapNorthToUp();
                        }
                        suppressReleaseTap = true;
                        SuppressUiClickFallout();
                        CancelWorldDragBox();
                    }
                }
            }

            if (mode == TouchMode.MapPan)
            {
                SuppressUiClickFallout();
                panGestureDistance = Mathf.Max(panGestureDistance, moveDistance);
                Vector2 panSample = PanWorldByGuiDelta(lastGui, gui);
                SampleWorldPanVelocity(panSample);
                CancelWorldDragBox();
            }

            if (mode == TouchMode.WorldDragBox)
            {
                // The drag-box rect is drawn in OnGUI (marching ants); the vanilla
                // WorldDragBox tracks start + current mouse position, and the touch
                // drives the OS mouse position, so SelectInsideDragBox on release
                // sees the same rect the user drew. Keep the box alive and do not pan.
                SuppressUiClickFallout();
                panGestureDistance = Mathf.Max(panGestureDistance, moveDistance);
            }

            if (IsEnded(touch))
            {
                if (mode == TouchMode.OneFinger && moveDistance <= DragSlopPixels && !rightClickDone)
                {
                    // Record the tap so the next touch can be recognised as the
                    // second tap of a double-tap (point-and-drag arming).
                    lastWorldTapTime = Time.realtimeSinceStartup;
                    lastWorldTapGui = gui;
                    DoWorldLeftClick(gui);
                }
                else if (mode == TouchMode.WorldDragBox)
                {
                    FinishWorldDragBox();
                }
                FinalizeInertiaFromGesture();
                FinishTouch();
                return;
            }

            lastGui = gui;
        }

        private static void UpdateTwoFinger(ContactTouch a, ContactTouch b)
        {
            Vector2 guiA = TouchToGui(a.position);
            Vector2 guiB = TouchToGui(b.position);
            Vector2 mid = (guiA + guiB) * 0.5f;
            float distance = Vector2.Distance(guiA, guiB);

            if (mode != TouchMode.TwoFinger)
            {
                if (IsLikelyUiPosition(guiA) || IsLikelyUiPosition(guiB))
                {
                    // Two fingers starting on a control window should not move
                    // the map or globe behind that window.
                    startedOverUi = true;
                    suppressReleaseTap = true;
                    mode = TouchMode.OneFinger;
                    StopInertia();
                    TouchTapRepair.Clear();
                    CancelRimWorldDragBox();
                    CancelWorldDragBox();
                    return;
                }
                mode = TouchMode.TwoFinger;
                GlobeTwist.Reset();
                GlobeTwist.CommitDegrees = RimTouchMod.Settings != null ? RimTouchMod.Settings.twistCommitDegrees : 10f;
                GlobeTwist.Update(guiA.x, guiA.y, guiB.x, guiB.y);
                suppressReleaseTap = true;
                SuppressUiClickFallout();
                lastTwoFingerMidpoint = mid;
                lastTwoFingerDistance = distance;
                twoFingerStartGuiA = guiA;
                twoFingerStartGuiB = guiB;
                twoFingerStartMidpoint = mid;
                twoFingerStartDistance = distance;
                twoFingerStartTime = Time.realtimeSinceStartup;
                twoFingerMaxMove = 0f;
                twoFingerMaxDistanceChange = 0f;
                twoFingerStartedWithVanillaMapTool = Find.CurrentMap != null && !IsWorldMapActive() && IsVanillaMapToolActive();
                twoFingerPanAnchorMap = IsWorldMapActive() ? Vector3.zero : (Find.CurrentMap != null ? GuiToMapPosition(mid) : Vector3.zero);
                lastPanSampleTime = Time.realtimeSinceStartup;
                lastWorldPanSampleTime = lastPanSampleTime;
                worldPanAnchorValid = IsWorldMapActive() && ShouldUseWorldAnchorPan() && TryGetWorldSpherePointAtGui(mid, out worldPanAnchorPoint);
                if (worldPanAnchorValid) UpdateWorldPanAnchorLocal();
                lastZoomSampleTime = lastPanSampleTime;
                panGestureDistance = 0f;
                ClearInertiaSamples();
                TouchTapRepair.Clear();
                CancelRimWorldDragBox();
                return;
            }

            bool ended = IsEnded(a) || IsEnded(b);
            twoFingerMaxMove = Mathf.Max(twoFingerMaxMove, Vector2.Distance(guiA, twoFingerStartGuiA), Vector2.Distance(guiB, twoFingerStartGuiB));
            twoFingerMaxDistanceChange = Mathf.Max(twoFingerMaxDistanceChange, Mathf.Abs(distance - twoFingerStartDistance));
            GlobeTwist.CommitDegrees = RimTouchMod.Settings != null ? RimTouchMod.Settings.twistCommitDegrees : 10f;
            float twistDegrees = ended ? 0 : GlobeTwist.Update(guiA.x, guiA.y, guiB.x, guiB.y);

            if (!ended && IsWorldMapActive())
            {
                SuppressUiClickFallout();
                panGestureDistance += Vector2.Distance(mid, lastTwoFingerMidpoint);
                Vector2 panSample = PanWorldByGuiDelta(lastTwoFingerMidpoint, mid);
                SampleWorldPanVelocity(panSample);
                ApplyWorldPinchZoom(lastTwoFingerDistance, distance);
                if (RimTouchMod.Settings.enableGlobeTwist && twistDegrees != 0 && ApplyWorldTwist(twistDegrees, mid))
                {
                    StopInertia();
                    ClearInertiaSamples();
                }
                CancelWorldDragBox();
            }
            else if (!ended && Find.CurrentMap != null)
            {
                SuppressUiClickFallout();
                Vector3 appliedDelta = KeepMapPointUnderGui(twoFingerPanAnchorMap, mid);
                panGestureDistance += Vector2.Distance(mid, lastTwoFingerMidpoint);
                SamplePanVelocityFromMapDelta(appliedDelta);
                ApplyPinchZoom(lastTwoFingerDistance, distance, mid);
                CancelRimWorldDragBox();
            }

            lastTwoFingerMidpoint = mid;
            lastTwoFingerDistance = distance;

            if (ended)
            {
                suppressReleaseTap = true;
                if (TryCancelVanillaMapToolFromTwoFingerTap() || TrySelectFromTwoFingerDraftedTap())
                {
                    StopInertia();
                }
                else
                {
                    FinalizeInertiaFromGesture();
                }
                FinishTouch();
            }
        }

        private static void FinalizeTouchDisappearance()
        {
            if (uiScrollGestureActive)
            {
                FinalizeUiScrollInertia();
                return;
            }

            if (mode == TouchMode.OneFinger)
            {
                if (startedOverUi)
                {
                    float moveDistance = Vector2.Distance(lastGui, startGui);
                    if (moveDistance <= DragSlopPixels && !suppressReleaseTap)
                    {
                        QueueUiTap(lastGui);
                    }
                }
                else
                {
                    float moveDistance = Vector2.Distance(lastGui, startGui);
                    if (moveDistance <= DragSlopPixels && !rightClickDone)
                    {
                        if (IsWorldMapActive())
                        {
                            DoWorldLeftClick(lastGui);
                        }
                        else if (!TryDoDraftedPawnTapCommand(lastGui))
                        {
                            DoMapLeftClick(lastGui);
                        }
                    }
                }
            }
            else if (mode == TouchMode.SelectionHold)
            {
                FinishRimWorldDragBox();
            }
            else if (mode == TouchMode.TwoFinger && TryCancelVanillaMapToolFromTwoFingerTap())
            {
                StopInertia();
                return;
            }
            else if (mode == TouchMode.TwoFinger && TrySelectFromTwoFingerDraftedTap())
            {
                StopInertia();
                return;
            }

            FinalizeInertiaFromGesture();
        }

        private static void UpdateEdgeScrollSuppression()
        {
            if (!suppressEdgeScrollUntilMouseMoves)
            {
                return;
            }

            if ((Input.mousePosition - mousePositionWhenTouchEnded).sqrMagnitude > 16f || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                suppressEdgeScrollUntilMouseMoves = false;
                suppressVanillaMapUntilMouseMoves = false;
            }
        }

        private static void QueueUiTap(Vector2 gui)
        {
            int clickCount = TouchTapRepair.QueueTap(gui);
            if (clickCount < 2 || !TryJumpToColonistBarTouchDoubleTap(gui))
            {
                return;
            }

            suppressReleaseTap = true;
            TouchTapRepair.Clear(false);
            SuppressUiClickFallout();
        }

        private static bool TryJumpToColonistBarTouchDoubleTap(Vector2 gui)
        {
            ColonistBar colonistBar = Find.ColonistBar;
            if (colonistBar == null)
            {
                return false;
            }

            Thing thing = colonistBar.ColonistOrCorpseAt(gui);
            Pawn pawn = thing as Pawn;
            if (pawn == null)
            {
                Corpse corpse = thing as Corpse;
                if (corpse != null)
                {
                    pawn = corpse.InnerPawn;
                }
            }

            if (pawn == null)
            {
                return false;
            }

            CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(pawn), CameraJumper.MovementMode.Pan);
            return true;
        }

        private static void DoMapLeftClick(Vector2 gui)
        {
            Map map = Find.CurrentMap;
            if (map == null || GetLocalSelector() == null)
            {
                return;
            }

            IntVec3 cell = GuiToMapPosition(gui).ToIntVec3();
            if (!cell.InBounds(map))
            {
                return;
            }

            int clickCount = TouchTapRepair.RegisterTap(gui);
            if (clickCount >= 2 && TryDoMapDoubleClick(gui, map))
            {
                return;
            }

            object selectable = FindSelectableObjectNearGui(gui, map);
            if (selectable != null)
            {
                GetLocalSelector().ClearSelection();
                GetLocalSelector().Select(selectable, true, true);
            }
            else
            {
                GetLocalSelector().ClearSelection();
            }
        }

        private static bool TryDoMapDoubleClick(Vector2 gui, Map map)
        {
            if (GetLocalSelector() == null || Find.CameraDriver == null || map == null)
            {
                return false;
            }

            Thing target = FindSelectableThingNearGui(gui, map);
            if (target == null)
            {
                return false;
            }

            CellRect viewRect = Find.CameraDriver.CurrentViewRect;
            List<Thing> allThings = map.listerThings != null ? map.listerThings.AllThings : null;
            if (allThings == null)
            {
                return false;
            }

            List<Thing> matches = new List<Thing>();
            for (int i = 0; i < allThings.Count; i++)
            {
                Thing thing = allThings[i];
                if (thing == null || !thing.Spawned || thing.Map != map || !viewRect.Contains(thing.Position))
                {
                    continue;
                }
                if (IsSameDoubleClickSelectionKind(target, thing))
                {
                    matches.Add(thing);
                }
            }

            if (matches.Count <= 0)
            {
                return false;
            }

            GetLocalSelector().ClearSelection();
            for (int i = 0; i < matches.Count; i++)
            {
                GetLocalSelector().Select(matches[i], i == 0, true);
            }
            return true;
        }

        private static object FindSelectableObjectNearGui(Vector2 gui, Map map)
        {
            if (map == null)
            {
                return null;
            }

            object best = null;
            float bestScore = float.MaxValue;
            HashSet<object> seen = new HashSet<object>();
            for (int i = 0; i < MapTargetSearchOffsets.Length; i++)
            {
                Vector2 sampleGui = gui + MapTargetSearchOffsets[i];
                IntVec3 cell = GuiToMapPosition(sampleGui).ToIntVec3();
                if (!cell.InBounds(map))
                {
                    continue;
                }

                IEnumerator<object> enumerator = Selector.SelectableObjectsAt(cell, map).GetEnumerator();
                try
                {
                    while (enumerator.MoveNext())
                    {
                        object candidate = enumerator.Current;
                        if (candidate == null || !seen.Add(candidate))
                        {
                            continue;
                        }

                        float score = GetSelectableGuiDistanceSquared(candidate, gui)
                            + MapTargetSearchOffsets[i].sqrMagnitude * 0.05f;
                        if (score < bestScore)
                        {
                            best = candidate;
                            bestScore = score;
                        }
                    }
                }
                finally
                {
                    enumerator.Dispose();
                }
            }

            return best;
        }

        private static Thing FindSelectableThingNearGui(Vector2 gui, Map map)
        {
            return FindSelectableObjectNearGui(gui, map) as Thing;
        }

        private static float GetSelectableGuiDistanceSquared(object selectable, Vector2 gui)
        {
            Thing thing = selectable as Thing;
            if (thing == null)
            {
                return 0f;
            }

            try
            {
                Vector2 ui = UI.MapToUIPosition(thing.DrawPos);
                Vector2 thingGui = new Vector2(ui.x, UI.screenHeight - ui.y);
                return (thingGui - gui).sqrMagnitude;
            }
            catch
            {
                return 0f;
            }
        }

        private static bool IsSameDoubleClickSelectionKind(Thing target, Thing candidate)
        {
            if (target == null || candidate == null || target.def != candidate.def)
            {
                return false;
            }

            Pawn targetPawn = target as Pawn;
            Pawn candidatePawn = candidate as Pawn;
            if (targetPawn != null || candidatePawn != null)
            {
                return targetPawn != null
                    && candidatePawn != null
                    && targetPawn.Faction == candidatePawn.Faction;
            }

            return true;
        }

        private static void DoMapRightClick(Vector2 gui)
        {
            Map map = Find.CurrentMap;
            if (map == null || GetLocalSelector() == null || GetLocalSelector().SelectedPawns == null || GetLocalSelector().SelectedPawns.Count == 0)
            {
                return;
            }

            Vector3 clickPosition = GuiToMapPosition(gui);
            FloatMenuContext context;
            List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(GetLocalSelector().SelectedPawns, clickPosition, out context);
            if (options != null && options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static bool TryDoDraftedPawnTapCommand(Vector2 gui)
        {
            if (!HasSelectedDraftedPawn())
            {
                return false;
            }

            Vector3 clickPosition = GuiToMapPosition(gui);
            FloatMenuContext context;
            List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(GetLocalSelector().SelectedPawns, clickPosition, out context);
            if (options == null || options.Count == 0)
            {
                return false;
            }

            FloatMenuOption autoTake = null;
            if (FloatMenuMakerMapGetAutoTakeOptionMethod != null)
            {
                object autoTakeValue;
                if (ReflectionGuard.TryInvoke(FloatMenuMakerMapGetAutoTakeOptionMethod, null, new object[] { options }, out autoTakeValue))
                {
                    autoTake = autoTakeValue as FloatMenuOption;
                }
            }

            if (autoTake == null || autoTake.Disabled || FloatMenuOptionActionField == null)
            {
                Find.WindowStack.Add(new FloatMenu(options));
                return true;
            }

            Action action;
            if (!ReflectionGuard.TryGetField(FloatMenuOptionActionField, autoTake, out action))
            {
                Find.WindowStack.Add(new FloatMenu(options));
                return true;
            }

            if (action == null)
            {
                Find.WindowStack.Add(new FloatMenu(options));
                return true;
            }

            action();
            return true;
        }

        private static bool HasSelectedDraftedPawn()
        {
            if (GetLocalSelector() == null || GetLocalSelector().SelectedPawns == null)
            {
                return false;
            }

            foreach (Pawn pawn in GetLocalSelector().SelectedPawns)
            {
                if (pawn != null && pawn.Drafted)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TrySelectFromTwoFingerDraftedTap()
        {
            if (!IsTwoFingerTapGesture() || Find.CurrentMap == null || IsWorldMapActive() || !HasSelectedDraftedPawn())
            {
                return false;
            }

            if (IsLikelyUiPosition(twoFingerStartGuiA) || IsLikelyUiPosition(twoFingerStartGuiB))
            {
                return false;
            }

            object selectable = FindSelectableObjectNearGui(twoFingerStartGuiA, Find.CurrentMap);
            if (selectable == null)
            {
                selectable = FindSelectableObjectNearGui(twoFingerStartGuiB, Find.CurrentMap);
            }
            if (selectable == null)
            {
                selectable = FindSelectableObjectNearGui(twoFingerStartMidpoint, Find.CurrentMap);
            }

            GetLocalSelector().ClearSelection();
            if (selectable != null)
            {
                GetLocalSelector().Select(selectable, true, true);
            }

            suppressReleaseTap = true;
            SuppressUiClickFallout();
            SuppressVanillaMapInputFallout();
            TouchTapRepair.Clear();
            CancelRimWorldDragBox();
            return true;
        }

        private static bool IsTwoFingerTapGesture()
        {
            float elapsed = Time.realtimeSinceStartup - twoFingerStartTime;
            return !GlobeTwist.HasTwisted && elapsed <= TwoFingerCancelTapSeconds
                && twoFingerMaxMove <= TwoFingerCancelMoveSlopPixels
                && twoFingerMaxDistanceChange <= TwoFingerCancelDistanceSlopPixels;
        }

        private static void BeginRimWorldDragBox()
        {
            DragBox dragBox = GetDragBox();
            if (dragBox == null)
            {
                return;
            }

            if (!ReflectionGuard.TrySetField(DragBoxStartField, dragBox, startMap))
            {
                return;
            }
            ReflectionGuard.TrySetField(DragBoxActiveField, dragBox, true);
        }

        private static void EnsureRimWorldDragBoxState()
        {
            if (mode != TouchMode.SelectionHold)
            {
                return;
            }

            DragBox dragBox = GetDragBox();
            if (dragBox != null)
            {
                ReflectionGuard.TrySetField(DragBoxActiveField, dragBox, true);
            }
        }

        private static void FinishRimWorldDragBox()
        {
            Selector selector = GetLocalSelector();
            DragBox dragBox = GetDragBox();
            if (selector == null || dragBox == null)
            {
                return;
            }

            try
            {
                ReflectionGuard.TryInvoke(SelectorSelectInsideDragBoxMethod, selector, null);
            }
            finally
            {
                ReflectionGuard.TrySetField(DragBoxActiveField, dragBox, false);
            }
        }

        private static void CancelRimWorldDragBox()
        {
            DragBox dragBox = GetDragBox();
            if (dragBox != null)
            {
                ReflectionGuard.TrySetField(DragBoxActiveField, dragBox, false);
            }
        }

        private static void CancelWorldDragBox()
        {
            if (WorldSelectorDragBoxField == null || WorldDragBoxActiveField == null)
            {
                return;
            }

            WorldSelector selector;
            try
            {
                selector = Find.WorldSelector;
            }
            catch
            {
                return;
            }

            if (selector == null)
            {
                return;
            }

            WorldDragBox dragBox;
            if (!ReflectionGuard.TryGetField(WorldSelectorDragBoxField, selector, out dragBox))
            {
                return;
            }
            if (dragBox != null)
            {
                ReflectionGuard.TrySetField(WorldDragBoxActiveField, dragBox, false);
            }
        }

        private static bool WorldPointDragEnabled()
        {
            return RimTouchMod.Settings != null && RimTouchMod.Settings.enableWorldPointDrag;
        }

        private static WorldDragBox GetWorldDragBox()
        {
            if (WorldSelectorDragBoxField == null)
            {
                return null;
            }

            WorldSelector selector;
            try
            {
                selector = Find.WorldSelector;
            }
            catch
            {
                return null;
            }

            if (selector == null)
            {
                return null;
            }

            WorldDragBox dragBox;
            return ReflectionGuard.TryGetField(WorldSelectorDragBoxField, selector, out dragBox) ? dragBox : null;
        }

        // Deliberate world drag-box (point-and-drag): double-tap, hold, drag on the
        // globe. Mirrors the local-map Begin/FinishRimWorldDragBox pair, but the world
        // box start is a screen/GUI position (UI.MousePositionOnUIInverted), not a map
        // position, and selection runs through WorldSelector.SelectInsideDragBox.
        private static void BeginWorldDragBox(Vector2 guiStart)
        {
            WorldDragBox dragBox = GetWorldDragBox();
            if (dragBox == null || WorldDragBoxStartField == null || WorldDragBoxActiveField == null)
            {
                return;
            }

            if (!ReflectionGuard.TrySetField(WorldDragBoxStartField, dragBox, guiStart))
            {
                return;
            }
            ReflectionGuard.TrySetField(WorldDragBoxActiveField, dragBox, true);
            worldDragBoxStartGui = guiStart;
        }

        private static void FinishWorldDragBox()
        {
            WorldSelector selector;
            try
            {
                selector = Find.WorldSelector;
            }
            catch
            {
                selector = null;
            }

            WorldDragBox dragBox = GetWorldDragBox();
            if (selector != null && dragBox != null && WorldSelectorSelectInsideDragBoxMethod != null)
            {
                try
                {
                    ReflectionGuard.TryInvoke(WorldSelectorSelectInsideDragBoxMethod, selector, null);
                }
                finally
                {
                    ReflectionGuard.TrySetField(WorldDragBoxActiveField, dragBox, false);
                }
            }
            else if (dragBox != null)
            {
                ReflectionGuard.TrySetField(WorldDragBoxActiveField, dragBox, false);
            }
        }

        // Marching-ants selection rectangle: translucent fill plus an animated dashed
        // border whose dashes travel around the perimeter (the selection-box animation
        // every touch canvas app uses: Photoshop, Figma, GIMP, macshot). The dash phase
        // advances with realtime so the ants keep marching while the finger is held.
        private static void DrawMarchingAntsRect(Rect rect)
        {
            if (rect.width < 2f || rect.height < 2f)
            {
                return;
            }

            Widgets.DrawRectFast(rect, new Color(0.6f, 0.8f, 1f, 0.10f));

            const float dash = 9f;
            const float gap = 7f;
            const float speed = 48f;
            float period = dash + gap;
            float phase = (Time.realtimeSinceStartup * speed) % period;
            Color border = new Color(1f, 1f, 1f, 0.95f);

            Vector2[] corners = new Vector2[5];
            corners[0] = new Vector2(rect.xMin, rect.yMin);
            corners[1] = new Vector2(rect.xMax, rect.yMin);
            corners[2] = new Vector2(rect.xMax, rect.yMax);
            corners[3] = new Vector2(rect.xMin, rect.yMax);
            corners[4] = corners[0];

            float distance = -phase;
            for (int e = 0; e < 4; e++)
            {
                Vector2 a = corners[e];
                Vector2 b = corners[e + 1];
                float edgeLen = Vector2.Distance(a, b);
                Vector2 dir = edgeLen > 0.001f ? (b - a) / edgeLen : Vector2.zero;
                float tt = distance;
                while (tt < edgeLen)
                {
                    float segStart = Mathf.Max(tt, 0f);
                    float segEnd = Mathf.Min(tt + dash, edgeLen);
                    if (segEnd > segStart)
                    {
                        Widgets.DrawLine(a + dir * segStart, a + dir * segEnd, border, 2f);
                    }
                    tt += period;
                }
                distance += edgeLen;
            }
        }

        private static Selector GetLocalSelector()
        {
            // Find.Selector casts Find.UIRoot to UIRoot_Play in 1.7. Entry menus
            // also receive touch/focus/resize events, so a null-selector check
            // after calling that accessor is too late.
            if (Current.ProgramState != ProgramState.Playing || !(Find.UIRoot is UIRoot_Play))
            {
                return null;
            }
            return Find.Selector;
        }
        private static DragBox GetDragBox()
        {
            Selector selector = GetLocalSelector();
            if (selector == null || SelectorDragBoxField == null)
            {
                return null;
            }
            DragBox dragBox;
            return ReflectionGuard.TryGetField(SelectorDragBoxField, selector, out dragBox) ? dragBox : null;
        }

        private static bool IsEnded(ContactTouch touch)
        {
            return touch.phase == ContactPhase.Ended;
        }

        private static void FinishTouch()
        {
            ResetState();
            hadTouchLastFrame = false;
        }

        private static void ResetState()
        {
            CancelRimWorldDragBox();
            CancelWorldDragBox();
            mode = TouchMode.None;
            primaryFingerId = -1;
            panAnchorMap = Vector3.zero;
            twoFingerPanAnchorMap = Vector3.zero;
            rightClickDone = false;
            suppressReleaseTap = false;
            uiScrollGestureActive = false;
            uiScrollActiveRect = default(Rect);
            uiScrollLastGui = Vector2.zero;
            lastUiScrollApplyFrame = -1000;
            worldPanAnchorValid = false;
            worldPanAnchorPoint = Vector3.zero;
            worldPanAnchorLocal = Vector3.zero;
            worldDoubleTapPrimed = false;
            worldDragBoxStartGui = Vector2.zero;
            panGestureDistance = 0f;
            lastSignificantMovementElapsed = 0f;
            selectPrimed = false;
            lastTwoFingerDistance = 0f;
            GlobeTwist.Reset();
            lastTwoFingerMidpoint = Vector2.zero;
            twoFingerStartGuiA = Vector2.zero;
            twoFingerStartGuiB = Vector2.zero;
            twoFingerStartDistance = 0f;
            twoFingerStartMidpoint = Vector2.zero;
            twoFingerStartTime = 0f;
            twoFingerMaxMove = 0f;
            twoFingerMaxDistanceChange = 0f;
            twoFingerStartedWithVanillaMapTool = false;
        }
    }
}
