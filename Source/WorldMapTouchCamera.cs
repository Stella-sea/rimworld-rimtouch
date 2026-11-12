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
            // Re-anchor after a twist without the roll constraint: the twist is
            // a deliberate rotation (including roll), and constraining the
            // re-anchor would fight it and make two-finger rotation misbehave.
            // Re-anchor after a twist with the free anchor rotation: the twist is
            // a deliberate rotation (including roll), and constraining the
            // re-anchor would fight it and make two-finger rotation misbehave.
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
                bool ok;
                if (LockNorthUpOnDragEnabled())
                {
                    // Projected turntable: the anchor rotation projected onto the
                    // turntable axes, so the grabbed point tracks the finger (grab
                    // error 8-10px instead of 163-217px) without rolling. Falls back
                    // to the fixed-k turntable when the anchor or raycast is missing.
                    ok = ApplyProjectedTurntableDrag(driver, currentGui)
                        || ApplyTurntableDrag(driver, guiDelta);
                }
                else
                {
                    ok = KeepWorldPointUnderGui(driver, worldPanAnchorPoint, currentGui);
                }
                if (ok)
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

        // The (clamped) world-space rotation that would keep the anchor point under
        // the finger, from real 3D geometry. Shared by the free anchor drag and the
        // constrained turntable drag below, so the two paths cannot drift apart.
        private static bool TryComputeWorldAnchorDelta(WorldCameraDriver driver, Vector3 anchorPoint, Vector2 gui, out Quaternion deltaRotation)
        {
            deltaRotation = Quaternion.identity;
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

            deltaRotation = Quaternion.FromToRotation(anchorNormal, currentNormal);
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

            return true;
        }

        private static bool KeepWorldPointUnderGui(WorldCameraDriver driver, Vector3 anchorPoint, Vector2 gui)
        {
            if (driver == null || WorldCameraSphereRotationField == null)
            {
                return false;
            }

            Quaternion deltaRotation;
            if (!TryComputeWorldAnchorDelta(driver, anchorPoint, gui, out deltaRotation))
            {
                return false;
            }

            Quaternion sphereRotation;
            if (!ReflectionGuard.TryGetField(WorldCameraSphereRotationField, driver, out sphereRotation))
            {
                return false;
            }

            // The anchor delta is a world-space rotation (it maps the anchor's
            // world direction onto the finger's), so it is pre-multiplied - the
            // same convention ApplyWorldTwist uses for view-space rotations.
            // Post-multiplication applied the delta in the sphere's local frame,
            // which let the grabbed point drift once the globe was already rotated.
            Quaternion newRotation = deltaRotation * sphereRotation;
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
        private static bool LockNorthUpOnDragEnabled()
        {
            return RimTouchMod.Settings != null && RimTouchMod.Settings.lockNorthUpOnDrag;
        }

        // Smooth return-to-north (test13, dual-axis off). The test12 instant
        // snap (setting lockNorthUp = true at drag start, which makes the
        // vanilla driver call RotateSoNorthIsUp(interpolate: false) every
        // frame) jumped straight to north and stuttered. Industry practice
        // for re-orienting a camera to north is an eased animation, not a
        // jump: Google Maps animates the camera (animateCamera with a
        // duration and an easeInOut interpolator) and never snaps the
        // heading; Unity's standard is a Slerp / critically-damped
        // approach toward the target orientation. This follows that: an
        // exponential approach of the sphere rotation toward the north-up
        // rotation, recomputed from the CURRENT rotation every frame, so
        // the return composes with an ongoing pan (hand-following) instead
        // of fighting it. Once aligned, the vanilla lock takes over.
        private static bool northReturnActive;
        private const float NorthReturnFinishDegrees = 0.5f;

        // The north-up rotation for the current view: same formula as
        // vanilla WorldCameraDriver.RotateSoNorthIsUp - the sphere rotation
        // that is north-up while looking at the same point.
        private static Quaternion ComputeNorthUpRotation(Quaternion sphereRotation)
        {
            return Quaternion.Inverse(Quaternion.LookRotation(Quaternion.Inverse(sphereRotation) * Vector3.forward));
        }

        private static void BeginNorthReturnIfNeeded()
        {
            if (RimTouchMod.Settings == null || !RimTouchMod.Settings.autoReturnNorth)
            {
                return;
            }
            WorldCameraDriver driver = Find.WorldCameraDriver;
            if (driver == null || WorldCameraSphereRotationField == null || Find.PlaySettings == null)
            {
                return;
            }
            if (Find.PlaySettings.lockNorthUp)
            {
                return;
            }
            Quaternion sphereRotation;
            if (!ReflectionGuard.TryGetField(WorldCameraSphereRotationField, driver, out sphereRotation))
            {
                return;
            }
            if (Quaternion.Angle(sphereRotation, ComputeNorthUpRotation(sphereRotation)) <= NorthReturnFinishDegrees)
            {
                Find.PlaySettings.lockNorthUp = true;
                return;
            }
            northReturnActive = true;
        }

        private static void UpdateNorthReturn()
        {
            if (!northReturnActive)
            {
                return;
            }
            if (LockNorthUpOnDragEnabled()
                || RimTouchMod.Settings == null || !RimTouchMod.Settings.autoReturnNorth
                || !IsWorldMapActive())
            {
                northReturnActive = false;
                return;
            }
            WorldCameraDriver driver = Find.WorldCameraDriver;
            if (driver == null || WorldCameraSphereRotationField == null || Find.PlaySettings == null)
            {
                northReturnActive = false;
                return;
            }
            if (Find.PlaySettings.lockNorthUp)
            {
                northReturnActive = false;
                return;
            }
            Quaternion sphereRotation;
            if (!ReflectionGuard.TryGetField(WorldCameraSphereRotationField, driver, out sphereRotation))
            {
                northReturnActive = false;
                return;
            }
            Quaternion target = ComputeNorthUpRotation(sphereRotation);
            if (Quaternion.Angle(sphereRotation, target) <= NorthReturnFinishDegrees)
            {
                Find.PlaySettings.lockNorthUp = true;
                northReturnActive = false;
                return;
            }
            // Exponential approach: move a frame-rate-independent fraction
            // of the remaining error each frame. tau is chosen so the
            // return is ~95% complete after the configured duration.
            // The target is recomputed from the CURRENT rotation each
            // frame, so the return follows a pan instead of fighting it.
            float duration = RimTouchMod.Settings.northReturnDuration;
            float tau = Mathf.Max(duration / 3f, 0.05f);
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
            float t = 1f - Mathf.Exp(-dt / tau);
            Quaternion next = Quaternion.Slerp(sphereRotation, target, t);
            if (!ReflectionGuard.TrySetField(WorldCameraSphereRotationField, driver, next))
            {
                northReturnActive = false;
                return;
            }
            SuppressWorldCameraEdgeScrollState(driver);
            if (WorldCameraApplyPositionMethod != null)
            {
                ReflectionGuard.TryInvoke(WorldCameraApplyPositionMethod, driver, null);
            }
        }

        private static void CancelNorthReturn()
        {
            northReturnActive = false;
        }

        // Turntable drag (Blender's default orbit, Fusion360 Constrained Orbit,
        // Google Maps): the finger's screen movement rotates the globe around two
        // axes only - the current north pole (horizontal drag spins the globe like
        // a desk globe) and the screen's horizontal axis (vertical drag tilts it).
        // Because the rotation is built from those two axes, it can never contain
        // roll, so north stays up and there is nothing to snap back when the drag
        // ends - unlike correcting the rotation afterwards, which fights the anchor
        // (each frame the anchor re-introduces the roll the correction removed, so
        // the view wobbles mid-drag and jerks straight on release). The amount uses
        // the same pixels-to-rotation calibration as the dolly path, so on-globe and
        // off-globe drags feel consistent. The grabbed point tracks the finger
        // approximately (a turntable has two degrees of freedom, so it cannot also
        // pin a point exactly); the closed loop re-evaluates every frame.
        // GUI Y points down. Dragging right (delta.x > 0) must move the globe face
        // right, dragging up (delta.y < 0) must move it up; see the sign note inside.
        // Records the grabbed point in sphere-LOCAL coords (a fixed geographic location)
        // from the world-space anchor captured at drag start. The projected turntable
        // needs the local form: each frame it recomputes the anchor CURRENT world
        // direction as sphereRotation * local, so the projection is a feedback loop.
        private static void UpdateWorldPanAnchorLocal()
        {
            WorldCameraDriver driver = Find.WorldCameraDriver;
            if (driver == null || WorldCameraSphereRotationField == null)
            {
                worldPanAnchorLocal = Vector3.zero;
                return;
            }
            Quaternion sphereRotation;
            if (!ReflectionGuard.TryGetField(WorldCameraSphereRotationField, driver, out sphereRotation))
            {
                worldPanAnchorLocal = Vector3.zero;
                return;
            }
            Vector3 center = GetWorldSphereCenter(driver);
            Vector3 anchorDir = worldPanAnchorPoint - center;
            if (anchorDir.sqrMagnitude <= 0.0001f)
            {
                worldPanAnchorLocal = Vector3.zero;
                return;
            }
            worldPanAnchorLocal = Quaternion.Inverse(sphereRotation) * anchorDir.normalized;
        }

        // Projected turntable (1c: grab-point tracking without roll). Per frame, compute
        // the full anchor rotation that would put the grabbed geographic point under the
        // finger, then project its angular velocity onto the turntable plane (north,
        // screen-horizontal) and discard the roll component. The anchor is tracked in
        // sphere-local coords, so each frame uses its CURRENT world direction: the
        // projection is a feedback loop that converges instead of accumulating error.
        // Simulation (anchor-test RunProjected): vertical drag grab error 163-217px
        // (fixed-k turntable) -> 8-10px, north tilt change 0.2-0.3 deg. A full circle
        // drag from a tilted start drifts the north up to ~4 deg (finite-rotation
        // composition); straight drags, the normal case, stay at 0.2-0.3 deg.
        // Returns false when the anchor or the finger raycast is unavailable, so the
        // caller can fall back to the fixed-k turntable for that frame.
        private static bool ApplyProjectedTurntableDrag(WorldCameraDriver driver, Vector2 currentGui)
        {
            if (driver == null || WorldCameraSphereRotationField == null)
            {
                return false;
            }
            Camera camera = GetWorldCamera(driver);
            if (camera == null)
            {
                return false;
            }
            if (worldPanAnchorLocal.sqrMagnitude <= 0.0001f)
            {
                return false;
            }
            Quaternion sphereRotation;
            if (!ReflectionGuard.TryGetField(WorldCameraSphereRotationField, driver, out sphereRotation))
            {
                return false;
            }
            Vector3 north = sphereRotation * Vector3.up;
            Vector3 pitchAxis = Vector3.Cross(north, camera.transform.forward);
            if (pitchAxis.sqrMagnitude <= 0.0001f)
            {
                pitchAxis = camera.transform.right;
            }
            pitchAxis.Normalize();
            if (north.sqrMagnitude <= 0.0001f || pitchAxis.sqrMagnitude <= 0.0001f)
            {
                return false;
            }
            north.Normalize();
            Vector3 targetPoint;
            if (!TryGetWorldSpherePointAtGui(currentGui, out targetPoint))
            {
                return false;
            }
            Vector3 center = GetWorldSphereCenter(driver);
            Vector3 targetDir = targetPoint - center;
            if (targetDir.sqrMagnitude <= 0.0001f)
            {
                return false;
            }
            targetDir.Normalize();
            Vector3 anchorDir = (sphereRotation * worldPanAnchorLocal).normalized;
            Quaternion anchorDelta = Quaternion.FromToRotation(anchorDir, targetDir);
            float angleDeg;
            Vector3 axis;
            anchorDelta.ToAngleAxis(out angleDeg, out axis);
            if (angleDeg <= 0.001f || axis.sqrMagnitude <= 0.0001f)
            {
                return true;
            }
            Vector3 omega = axis.normalized * (angleDeg * Mathf.Deg2Rad);
            Vector3 omegaTurn = north * Vector3.Dot(omega, north) + pitchAxis * Vector3.Dot(omega, pitchAxis);
            // The projected turntable is a feedback loop: omegaTurn is the
            // EXACT rotation that puts the grabbed point under the finger.
            // Scaling it by a sensitivity above 1 overshoots the finger
            // every frame and the loop oscillates (jitter). Clamp the gain
            // to 1 so the loop can never overshoot; below 1 it just tracks
            // more lazily, which stays stable. (The fixed-k fallback is
            // open-loop, so it keeps the full sensitivity range.)
            float sensitivity = RimTouchMod.Settings != null ? RimTouchMod.Settings.turntableDragSensitivity : 1f;
            float turnAngleDeg = omegaTurn.magnitude * Mathf.Rad2Deg * Mathf.Min(sensitivity, 1f);
            if (turnAngleDeg <= 0.001f)
            {
                return true;
            }
            turnAngleDeg = Mathf.Min(turnAngleDeg, 45f);
            Quaternion dR = Quaternion.AngleAxis(turnAngleDeg, omegaTurn.normalized);
            if (RimTouchMod.Settings != null && RimTouchMod.Settings.invertTurntableAxis)
            {
                dR = Quaternion.Inverse(dR);
            }
            Quaternion newRotation = dR * sphereRotation;
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

        private static bool ApplyTurntableDrag(WorldCameraDriver driver, Vector2 guiDelta)
        {
            if (driver == null || WorldCameraSphereRotationField == null)
            {
                return false;
            }
            Camera camera = GetWorldCamera(driver);
            if (camera == null)
            {
                return false;
            }
            Quaternion sphereRotation;
            if (!ReflectionGuard.TryGetField(WorldCameraSphereRotationField, driver, out sphereRotation))
            {
                return false;
            }
            Vector3 north = sphereRotation * Vector3.up;
            // Pitch axis: the screen-horizontal axis PERPENDICULAR to north.
            // camera.transform.right is only perpendicular to north while north is up;
            // once the north is tilted (after a twist), pitching around it rolls the
            // north (the tilted-start simulation drifted 6.8 deg). cross(north, forward)
            // stays perpendicular to north, so the turntable stays roll-free in both
            // directions. It equals screen-right while north is up. Falls back to
            // screen-right if the camera looks straight down the north pole.
            Vector3 pitchAxis = Vector3.Cross(north, camera.transform.forward);
            if (pitchAxis.sqrMagnitude <= 0.0001f)
            {
                pitchAxis = camera.transform.right;
            }
            pitchAxis.Normalize();
            if (north.sqrMagnitude <= 0.0001f || pitchAxis.sqrMagnitude <= 0.0001f)
            {
                return false;
            }
            float altitudeFactor = Mathf.Lerp(0.4f, 1.4f, Mathf.Clamp01(driver.AltitudePercent));
            float sensitivity = RimTouchMod.Settings != null ? RimTouchMod.Settings.turntableDragSensitivity : 1f;
            float degreesPerPixel = WorldPanPixelsToRotation * altitudeFactor * sensitivity;
            // Direction: the camera sits at -Z looking toward +Z, so the globe face
            // under the finger is on the -Z side; a positive spin around the north pole
            // moves that face LEFT, i.e. opposite to the finger. The anchor drag
            // (KeepWorldPointUnderGui) proves the correct direction: the turntable must
            // apply the INVERSE of the naive yaw*pitch rotation. A quaternion inverse
            // reverses the multiplication order, so the correct rotation is
            // pitch^-1 * yaw^-1 (not yaw^-1 * pitch^-1): merely negating both angles in
            // the original order re-introduces roll (the tilted-start simulation drifted
            // 6.8 deg). The "invert rotation axis" setting restores the old behaviour.
            Quaternion yaw = Quaternion.AngleAxis(guiDelta.x * degreesPerPixel, north.normalized);
            Quaternion pitch = Quaternion.AngleAxis(guiDelta.y * degreesPerPixel, pitchAxis);
            Quaternion newRotation;
            if (RimTouchMod.Settings != null && RimTouchMod.Settings.invertTurntableAxis)
            {
                newRotation = yaw * pitch * sphereRotation;
            }
            else
            {
                newRotation = Quaternion.Inverse(pitch) * Quaternion.Inverse(yaw) * sphereRotation;
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
