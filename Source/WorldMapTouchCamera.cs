using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using RimTouch.TouchPlatform;

namespace RimTouch
{
    public static partial class TouchInputDriver
    {
        private static bool ApplyWorldTwist(float clockwiseDegrees, Vector2 gui)
        {
            WorldCameraDriver driver = Find.WorldCameraDriver;
            Quaternion rotation;
            if (driver == null || !ReflectionGuard.TryGetField(WorldCameraSphereRotationField, driver, out rotation)) return false;
            Vector3 anchor;
            bool hasAnchor = TryGetWorldSpherePointAtGui(gui, out anchor);
            // ApplyPositionToGameObject uses inverse(sphereRotation) as the camera
            // rotation. Pre-multiplication therefore rolls in view space; GUI Y
            // points down, so a clockwise finger turn uses a negative Z angle.
            Quaternion next = Quaternion.AngleAxis(-clockwiseDegrees, Vector3.forward) * rotation;
            float angle = Quaternion.Angle(Quaternion.identity, next);
            if (float.IsNaN(angle) || float.IsInfinity(angle)) return false;
            if (!ReflectionGuard.TrySetField(WorldCameraSphereRotationField, driver, next)) return false;
            if (Find.PlaySettings != null) Find.PlaySettings.lockNorthUp = false;
            ReflectionGuard.TrySetField(WorldRotationAnimationLerpField, driver, 1f);
            SuppressWorldCameraEdgeScrollState(driver);
            if (WorldCameraApplyPositionMethod != null) ReflectionGuard.TryInvoke(WorldCameraApplyPositionMethod, driver, null);
            if (hasAnchor) KeepWorldPointUnderGui(driver, anchor, gui);
            TouchInputProof.RecordTwist(clockwiseDegrees);
            return true;
        }

        private static void ApplyWorldPinchZoom(float previousDistance, float currentDistance)
        {
            if (previousDistance <= 1f || currentDistance <= 1f)
            {
                return;
            }

            WorldCameraDriver driver = Find.WorldCameraDriver;
            if (driver == null)
            {
                return;
            }

            float currentAltitude;
            if (!TryGetWorldAltitude(driver, out currentAltitude))
            {
                return;
            }

            float zoomRatio = previousDistance / currentDistance;
            float maxAltitude = GetWorldMaxAltitude(driver);
            float targetAltitude = Mathf.Clamp(currentAltitude * zoomRatio, WorldCameraDriver.MinAltitude, maxAltitude);
            if (!TrySetWorldAltitude(driver, targetAltitude))
            {
                return;
            }
            zoomInertiaAnchorValid = false;
            SampleZoomVelocity(currentAltitude, targetAltitude);
            ReflectionGuard.TrySetField(WorldCameraMouseBottomEdgeStartField, driver, 0f);
        }

        private static bool IsWorldMapActive()
        {
            if (Find.WorldCameraDriver == null)
            {
                return false;
            }

            try
            {
                if (Find.WorldSelector == null)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            bool renderedNow;
            if (TryGetWorldRenderedNow(out renderedNow))
            {
                return renderedNow;
            }

            if (Find.World != null && Find.World.renderer != null)
            {
                object renderer = Find.World.renderer;
                object wantedMode = GetMemberValue(renderer, "wantedMode");
                if (wantedMode != null && wantedMode.ToString() != "None")
                {
                    return true;
                }
            }

            return Find.CurrentMap == null;
        }

        private static bool TryGetWorldRenderedNow(out bool renderedNow)
        {
            renderedNow = false;

            try
            {
                if (WorldRenderedNowProperty != null)
                {
                    return ReflectionGuard.TryGetProperty(WorldRenderedNowProperty, null, out renderedNow);
                }

                if (WorldRenderedNowGetter != null)
                {
                    object value;
                    if (ReflectionGuard.TryInvoke(WorldRenderedNowGetter, null, null, out value) && value is bool)
                    {
                        renderedNow = (bool)value;
                        return true;
                    }
                }

                if (WorldRenderedNowField != null)
                {
                    return ReflectionGuard.TryGetField(WorldRenderedNowField, null, out renderedNow);
                }
            }
            catch
            {
                renderedNow = false;
            }

            return false;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null)
            {
                return null;
            }

            Type type = instance.GetType();
            FieldInfo field = ReflectionGuard.Field(type, memberName, false);
            if (field != null)
            {
                object value;
                return ReflectionGuard.TryGetField(field, instance, out value) ? value : null;
            }

            PropertyInfo property = ReflectionGuard.Property(type, memberName, false);
            if (property != null)
            {
                object value;
                return ReflectionGuard.TryGetProperty(property, instance, out value) ? value : null;
            }

            return null;
        }

        private static Vector2 PanWorldByGuiDelta(Vector2 previousGui, Vector2 currentGui)
        {
            WorldCameraDriver driver = Find.WorldCameraDriver;
            if (driver == null)
            {
                return Vector2.zero;
            }

            Vector2 guiDelta = currentGui - previousGui;
            if (worldPanAnchorValid)
            {
                if (KeepWorldPointUnderGui(driver, worldPanAnchorPoint, currentGui))
                {
                    worldInertiaAnchorValid = true;
                    worldInertiaAnchorPoint = worldPanAnchorPoint;
                    worldInertiaGui = currentGui;
                }
                else
                {
                    worldInertiaAnchorValid = false;
                }
                return guiDelta;
            }

            Vector2 rotationDelta = GetWorldRotationDeltaFromGuiDelta(driver, previousGui, currentGui);
            ApplyWorldCameraDolly(driver, rotationDelta);
            worldInertiaAnchorValid = false;
            return guiDelta;
        }

        private static Vector2 GetWorldRotationDeltaFromGuiDelta(WorldCameraDriver driver, Vector2 previousGui, Vector2 currentGui)
        {
            Vector2 delta = currentGui - previousGui;
            float altitudeFactor = Mathf.Lerp(0.4f, 1.4f, Mathf.Clamp01(driver.AltitudePercent));
            return new Vector2(delta.x, -delta.y) * WorldPanPixelsToRotation * altitudeFactor;
        }

        private static bool KeepWorldPointUnderGui(WorldCameraDriver driver, Vector3 anchorPoint, Vector2 gui)
        {
            if (driver == null || WorldCameraSphereRotationField == null)
            {
                return false;
            }

            Vector3 currentPoint;
            if (!TryGetWorldSpherePointAtGui(gui, out currentPoint))
            {
                return false;
            }

            Vector3 center = GetWorldSphereCenter(driver);
            Vector3 anchorDir = anchorPoint - center;
            Vector3 currentDir = currentPoint - center;
            if (anchorDir.sqrMagnitude <= 0.0001f || currentDir.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Vector3 anchorNormal = anchorDir.normalized;
            Vector3 currentNormal = currentDir.normalized;
            float poleFactor = Mathf.Max(Mathf.Abs(anchorNormal.y), Mathf.Abs(currentNormal.y), GetWorldPoleFactor(driver));

            Quaternion sphereRotation;
            if (!ReflectionGuard.TryGetField(WorldCameraSphereRotationField, driver, out sphereRotation))
            {
                return false;
            }

            Quaternion deltaRotation = Quaternion.FromToRotation(anchorNormal, currentNormal);
            float rotationAngle = Quaternion.Angle(Quaternion.identity, deltaRotation);
            if (float.IsNaN(rotationAngle) || float.IsInfinity(rotationAngle))
            {
                return false;
            }

            // The cap below used to be a flat per-frame degree limit applied to every swipe,
            // anywhere on the globe. Any frame-rate dip or batched touch sample (multiple
            // finger moves collapsed into a single Update by Unity) needed more rotation than
            // that single-frame budget, got clamped, and then had to "catch up" over the next
            // frames - which looked like the globe panning in jagged staircase steps. Scale the
            // cap by how much time actually passed this frame, and only fall back to the tight
            // pole-safety cap close to the poles (as intended), so normal swipes away from the
            // poles are effectively unclamped.
            float frameSeconds = Mathf.Max(Time.unscaledDeltaTime, 1f / 240f);
            float velocityScale = frameSeconds / (1f / 60f);
            float poleDamping = Mathf.InverseLerp(WorldAnchorPoleDampingStart, 1f, poleFactor);
            float maxRotationDegrees = Mathf.Lerp(MaxWorldAnchorRotationDegrees * 8f, MaxWorldAnchorRotationDegrees, poleDamping) * velocityScale;
            if (rotationAngle > maxRotationDegrees)
            {
                deltaRotation = Quaternion.Slerp(Quaternion.identity, deltaRotation, maxRotationDegrees / rotationAngle);
            }

            if (poleDamping > 0f)
            {
                deltaRotation = Quaternion.Slerp(Quaternion.identity, deltaRotation, Mathf.Lerp(1f, WorldAnchorPoleMinimumScale, poleDamping));
            }

            // The anchor delta is a world-space rotation (it maps the anchor's
            // world direction onto the finger's), so it is pre-multiplied - the
            // same convention ApplyWorldTwist uses for view-space rotations.
            // Post-multiplication applied the delta in the sphere's local frame,
            // which let the grabbed point drift once the globe was already rotated.
            Quaternion newRotation = deltaRotation * sphereRotation;
            if (RimTouchMod.Settings != null && RimTouchMod.Settings.lockNorthUpOnDrag)
            {
                newRotation = LockNorthUp(newRotation, GetWorldCamera(driver));
            }
            if (!ReflectionGuard.TrySetField(WorldCameraSphereRotationField, driver, newRotation))
            {
                return false;
            }
            SuppressWorldCameraEdgeScrollState(driver);
            if (WorldCameraApplyPositionMethod != null)
            {
                ReflectionGuard.TryInvoke(WorldCameraApplyPositionMethod, driver, null);
            }
            return true;
        }

        // Keeps the globe's north pole pointing up on screen while dragging.
        // The anchor delta (FromToRotation) rotates around an axis through the
        // anchor and the finger point; when the drag is off-centre that axis has
        // a component along the view direction, so the view rolls as well as pans:
        // a straight upward drag also spins the map, and dragging in a small
        // circle spins the whole view. Project the north pole onto the screen,
        // measure how far it has rolled from screen-up, and counter-rotate around
        // the view axis. This is the same "lock north up" the base game offers;
        // the correction is a view-space rotation (pre-multiplication), the same
        // convention ApplyWorldTwist uses. When the camera looks straight at a
        // pole the projection degenerates and the correction is skipped.
        private static Quaternion LockNorthUp(Quaternion rotation, Camera camera)
        {
            if (camera == null)
            {
                return rotation;
            }
            Vector3 forward = camera.transform.forward;
            Vector3 up = camera.transform.up;
            if (forward.sqrMagnitude <= 0.0001f || up.sqrMagnitude <= 0.0001f)
            {
                return rotation;
            }
            forward = forward.normalized;
            up = up.normalized;
            Vector3 northWorld = rotation * Vector3.up;
            Vector3 northOnScreen = northWorld - forward * Vector3.Dot(northWorld, forward);
            if (northOnScreen.sqrMagnitude <= 0.0001f)
            {
                return rotation;
            }
            float roll = Vector3.SignedAngle(up, northOnScreen.normalized, forward);
            if (float.IsNaN(roll) || float.IsInfinity(roll) || Mathf.Abs(roll) < 0.01f)
            {
                return rotation;
            }
            return Quaternion.AngleAxis(-roll, forward) * rotation;
        }

        private static bool TryGetWorldSpherePointAtGui(Vector2 gui, out Vector3 point)
        {
            point = Vector3.zero;
            WorldCameraDriver driver = Find.WorldCameraDriver;
            Camera camera = GetWorldCamera(driver);
            if (driver == null || camera == null)
            {
                return false;
            }

            Vector3 screenPoint = GuiToScreenPoint(gui);
            Ray ray = camera.ScreenPointToRay(screenPoint);
            Vector3 center = GetWorldSphereCenter(driver);
            float radius = GetWorldSphereRadius(driver);
            Vector3 originToCenter = ray.origin - center;
            float b = Vector3.Dot(originToCenter, ray.direction);
            float c = originToCenter.sqrMagnitude - radius * radius;
            float discriminant = b * b - c;
            if (discriminant < 0f)
            {
                return false;
            }

            float root = Mathf.Sqrt(discriminant);
            float distance = -b - root;
            if (distance < 0f)
            {
                distance = -b + root;
            }
            if (distance < 0f)
            {
                return false;
            }

            point = ray.origin + ray.direction * distance;
            return IsFinite(point);
        }

        private static Vector3 GetWorldSphereCenter(WorldCameraDriver driver)
        {
            if (driver != null && WorldCameraLayerOriginOffsetField != null)
            {
                Vector3 value;
                if (ReflectionGuard.TryGetField(WorldCameraLayerOriginOffsetField, driver, out value))
                {
                    return value;
                }
            }
            return Vector3.zero;
        }

        private static float GetWorldSphereRadius(WorldCameraDriver driver)
        {
            if (WorldCameraSphereRadiusField != null)
            {
                float value;
                if (ReflectionGuard.TryGetField(WorldCameraSphereRadiusField, WorldCameraSphereRadiusField.IsStatic ? null : driver, out value) && value > 0f)
                {
                    return value;
                }
            }
            return 100f;
        }

        private static float GetWorldPoleFactor(WorldCameraDriver driver)
        {
            if (driver == null || WorldCameraSphereRotationField == null)
            {
                return 0f;
            }

            Quaternion sphereRotation;
            if (!ReflectionGuard.TryGetField(WorldCameraSphereRotationField, driver, out sphereRotation))
            {
                return 0f;
            }

            Vector3 lookPoint = -(Quaternion.Inverse(sphereRotation) * Vector3.forward);
            if (!IsFinite(lookPoint) || lookPoint.sqrMagnitude <= 0.0001f)
            {
                return 0f;
            }
            return Mathf.Abs(lookPoint.normalized.y);
        }

        private static bool ShouldDisableWorldPanInertia(WorldCameraDriver driver)
        {
            return driver == null || !worldInertiaAnchorValid || GetWorldPoleFactor(driver) >= WorldPoleInertiaDisable;
        }

        private static bool ShouldUseWorldAnchorPan()
        {
            // The anchor pan rotates the globe sphere directly (the same
            // mechanism the two-finger twist uses, and twist demonstrably
            // works on the landing-site page), so it only needs the world
            // map to be active - not a colony or Playing state. The old gate
            // (Playing + CurrentMap != null) forced the landing-site page
            // onto the dolly path, and on that page the injected dolly never
            // moves the globe, so one-finger drags did nothing at all.
            // If the anchor raycast misses (finger off the globe), the
            // caller still falls back to the dolly path as before.
            return IsWorldMapActive();
        }

        private static bool TryGetWorldAltitude(WorldCameraDriver driver, out float altitude)
        {
            altitude = 0f;
            return driver != null && ReflectionGuard.TryGetField(WorldCameraAltitudeField, driver, out altitude);
        }

        private static bool TrySetWorldAltitude(WorldCameraDriver driver, float altitude)
        {
            if (driver == null || WorldCameraAltitudeField == null || WorldCameraDesiredAltitudeField == null)
            {
                return false;
            }

            bool altitudeSet = ReflectionGuard.TrySetField(WorldCameraAltitudeField, driver, altitude);
            bool desiredAltitudeSet = ReflectionGuard.TrySetField(WorldCameraDesiredAltitudeField, driver, altitude);
            return altitudeSet && desiredAltitudeSet;
        }

        private static float GetWorldMaxAltitude(WorldCameraDriver driver)
        {
            float maxAltitude;
            if (driver != null
                && ReflectionGuard.TryGetField(WorldCameraMaxAltitudeField, driver, out maxAltitude)
                && maxAltitude > WorldCameraDriver.MinAltitude)
            {
                return maxAltitude;
            }

            return 75f;
        }

        private static float GetWorldCloseZoomFactor(WorldCameraDriver driver)
        {
            if (driver == null)
            {
                return 0f;
            }
            return 1f - Mathf.Clamp01(driver.AltitudePercent);
        }

        private static float GetWorldPanInertiaStartSpeed(WorldCameraDriver driver)
        {
            return Mathf.Lerp(MinWorldPanInertiaStartSpeed, WorldCloseZoomInertiaStartSpeed, GetWorldCloseZoomFactor(driver));
        }

        private static float GetWorldPanInertiaStopSpeed(WorldCameraDriver driver)
        {
            return Mathf.Lerp(MinWorldPanInertiaSpeed, WorldCloseZoomInertiaStopSpeed, GetWorldCloseZoomFactor(driver));
        }

        private static float GetWorldPanInertiaDamping(WorldCameraDriver driver)
        {
            return PanInertiaDamping * Mathf.Lerp(1f, WorldCloseZoomInertiaDampingScale, GetWorldCloseZoomFactor(driver));
        }

        private static float GetWorldPanInertiaMoveScale(WorldCameraDriver driver)
        {
            return Mathf.Lerp(1f, WorldCloseZoomInertiaMoveScale, GetWorldCloseZoomFactor(driver));
        }

        private static Camera GetWorldCamera(WorldCameraDriver driver)
        {
            if (driver == null || WorldCameraCachedCameraField == null)
            {
                return null;
            }

            Camera camera;
            return ReflectionGuard.TryGetField(WorldCameraCachedCameraField, driver, out camera) ? camera : null;
        }

        private static void ApplyWorldCameraDolly(WorldCameraDriver driver, Vector2 dolly)
        {
            if (float.IsNaN(dolly.x) || float.IsInfinity(dolly.x) || float.IsNaN(dolly.y) || float.IsInfinity(dolly.y))
            {
                return;
            }

            if (driver == null)
            {
                return;
            }

            Vector2 pending = pendingWorldCameraDolly + dolly;
            if (!IsFinite(pending))
            {
                pendingWorldCameraDolly = Vector2.zero;
                return;
            }

            pendingWorldCameraDolly = pending;
        }

        public static Vector2 ModifyWorldCameraInput(WorldCameraDriver driver, Vector2 vanillaInput)
        {
            if (driver == null || !TouchModeEnabled || !IsWorldMapActive())
            {
                pendingWorldCameraDolly = Vector2.zero;
                return vanillaInput;
            }

            Vector2 touchInput = pendingWorldCameraDolly;
            pendingWorldCameraDolly = Vector2.zero;

            if (touchInput.sqrMagnitude > 0.000001f || ShouldSuppressWorldEdgeScroll)
            {
                SuppressWorldCameraEdgeScrollState(driver);
                return touchInput;
            }

            return vanillaInput;
        }

        public static void SuppressWorldCameraEdgeScrollState(WorldCameraDriver driver)
        {
            if (driver == null)
            {
                return;
            }

            ReflectionGuard.TrySetField(WorldCameraRotationVelocityField, driver, Vector2.zero);
            ReflectionGuard.TrySetField(WorldCameraMouseBottomEdgeStartField, driver, 0f);
        }

        private static void DoWorldLeftClick(Vector2 gui)
        {
            WorldSelector selector = Find.WorldSelector;
            if (selector == null)
            {
                return;
            }

            PlanetTile tile;
            if (TryGetWorldTileAtGui(gui, out tile))
            {
                selector.SelectFirstOrNextAt(tile);
                return;
            }

            List<WorldObject> objects = GenWorldUI.WorldObjectsUnderMouse(gui);
            if (objects != null && objects.Count > 0)
            {
                selector.ClearSelection();
                selector.Select(objects[0], true);
                return;
            }

            selector.ClearSelection();
        }

        private static bool TryGetWorldTileAtGui(Vector2 gui, out PlanetTile tile)
        {
            tile = PlanetTile.Invalid;

            if (Find.World == null || Find.World.renderer == null)
            {
                return false;
            }

            WorldCameraDriver driver = Find.WorldCameraDriver;
            Camera camera = GetWorldCamera(driver);
            if (camera == null)
            {
                return false;
            }

            try
            {
                Ray ray = camera.ScreenPointToRay(GuiToScreenPoint(gui));
                RaycastHit hit;
                bool hitWorld = Physics.Raycast(ray, out hit, WorldCameraManager.FarClipPlane, WorldCameraManager.WorldLayerMask);
                if (!hitWorld)
                {
                    // Preserve Unity's former default mask: exclude Ignore Raycast (layer 2).
                    hitWorld = Physics.Raycast(ray, out hit, WorldCameraManager.FarClipPlane, ~(1 << 2));
                }
                if (!hitWorld)
                {
                    return false;
                }

                tile = Find.World.renderer.GetTileFromRayHit(hit);
                return tile.Valid;
            }
            catch
            {
                tile = PlanetTile.Invalid;
                return false;
            }
        }
    }
}
