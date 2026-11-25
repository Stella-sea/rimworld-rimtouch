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
            if (driver == null) return false;
            Quaternion rotation = driver.sphereRotation;
            Vector3 anchor = Vector3.zero;
            bool hasAnchor = !LockNorthUpOnDragEnabled() && TryGetWorldSpherePointAtGui(gui, out anchor);
            // ApplyPositionToGameObject uses inverse(sphereRotation) as the camera
            // rotation. Pre-multiplication therefore rolls in view space; GUI Y
            // points down, so a clockwise finger turn uses a negative Z angle.
            Quaternion next = Quaternion.AngleAxis(-clockwiseDegrees, Vector3.forward) * rotation;
            float angle = Quaternion.Angle(Quaternion.identity, next);
            if (float.IsNaN(angle) || float.IsInfinity(angle)) return false;
            if (!CommitWorldRotation(driver, next)) return false;
            if (Find.PlaySettings != null) Find.PlaySettings.lockNorthUp = false;
            // Dual-axis twist is pure view roll. Only free mode re-anchors
            // around the midpoint; that correction also changes the view direction.
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
            try
            {
                return Find.WorldCameraDriver != null
                    && Find.WorldSelector != null
                    && WorldRendererUtility.WorldRendered;
            }
            catch
            {
                return false;
            }
        }

        private const float WorldFreeLimbDampStartCos = 0.42f;
        private const float WorldFreeLimbReleaseCos = 0.12f;
        private const float WorldFreeReanchorCos = 0.28f;
        private const float WorldFreeLimbOuterSpeedRatio = 1.0f;
        private const float WorldFreeLimbInnerSpeedRatio = 2.4f;

        private static Vector2 PanWorldByGuiDelta(Vector2 previousGui, Vector2 currentGui)
        {
            WorldCameraDriver driver = Find.WorldCameraDriver;
            if (driver == null)
            {
                return Vector2.zero;
            }

            Vector2 guiDelta = currentGui - previousGui;
            if (LockNorthUpOnDragEnabled())
            {
                // The same incremental orbit applies inside and outside the globe.
                // Tap/drag classification stays in UpdateOneFingerWorld, so an
                // outside tap can still clear selection without moving the camera.
                worldInertiaAnchorValid = ApplyDualAxisTurntableDrag(driver, guiDelta);
                worldInertiaGui = currentGui;
                return guiDelta;
            }

            if (!worldFreeStartedOnGlobe)
            {
                // Free mode requires grabbing the globe itself; starting a drag
                // in empty space outside the globe must not rotate it.
                worldInertiaAnchorValid = false;
                return Vector2.zero;
            }

            return PanWorldFreeByGuiDelta(driver, previousGui, currentGui);
        }

        private static Vector2 PanWorldFreeByGuiDelta(WorldCameraDriver driver, Vector2 previousGui, Vector2 currentGui)
        {
            Vector2 guiDelta = currentGui - previousGui;
            Vector3 currentHit;
            float cosIncidence;
            bool hitSphere = TryGetWorldSpherePointAtGui(currentGui, out currentHit, out cosIncidence);

            if (!worldPanAnchorValid)
            {
                // Finger previously slipped off the globe limb. Re-grab cleanly
                // once back inside the hysteresis threshold without snapping to
                // the old start anchor.
                if (hitSphere && cosIncidence >= WorldFreeReanchorCos)
                {
                    worldFreeLimbCoasting = false;
                    worldPanVelocity = Vector2.zero;
                    ClearInertiaSamples();
                    worldPanAnchorPoint = currentHit;
                    worldPanAnchorValid = true;
                    worldInertiaAnchorValid = true;
                    worldInertiaAnchorPoint = currentHit;
                    worldInertiaGui = currentGui;
                    lastWorldPanSampleTime = Time.realtimeSinceStartup;
                    return Vector2.zero;
                }

                // Still outside the globe: let slip-off momentum coast smoothly,
                // without letting empty-space movement push the globe.
                if (worldFreeLimbCoasting)
                {
                    float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.05f);
                    StepWorldFreeCoast(driver, dt);
                }
                return Vector2.zero;
            }

            if (!hitSphere || cosIncidence <= WorldFreeLimbReleaseCos)
            {
                BeginWorldFreeLimbCoast(driver, guiDelta);
                if (worldFreeLimbCoasting)
                {
                    float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.05f);
                    StepWorldFreeCoast(driver, dt);
                }
                return Vector2.zero;
            }

            if (cosIncidence >= WorldFreeLimbDampStartCos)
            {
                if (KeepWorldPointUnderGui(driver, worldPanAnchorPoint, currentGui))
                {
                    worldInertiaAnchorValid = true;
                    worldInertiaAnchorPoint = worldPanAnchorPoint;
                    worldInertiaGui = currentGui;
                    return guiDelta;
                }

                BeginWorldFreeLimbCoast(driver, guiDelta);
                return Vector2.zero;
            }

            // Outer rim band: preserve the spherical grab direction while smoothly
            // capping the grazing-ray singularity, then re-sync the anchor under
            // the finger so reversing inward never snaps.
            if (ApplyRimDampedWorldAnchor(driver, worldPanAnchorPoint, guiDelta, currentGui, cosIncidence))
            {
                Vector3 syncedPoint;
                if (TryGetWorldSpherePointAtGui(currentGui, out syncedPoint))
                {
                    worldPanAnchorPoint = syncedPoint;
                }
                worldInertiaAnchorValid = true;
                worldInertiaAnchorPoint = worldPanAnchorPoint;
                worldInertiaGui = currentGui;
                return guiDelta;
            }

            BeginWorldFreeLimbCoast(driver, guiDelta);
            return Vector2.zero;
        }

        private static bool ApplyRimDampedWorldAnchor(WorldCameraDriver driver, Vector3 anchorPoint, Vector2 guiDelta, Vector2 currentGui, float cosIncidence)
        {
            if (driver == null)
            {
                return false;
            }
            if (guiDelta.sqrMagnitude <= 0.00000001f)
            {
                return true;
            }

            Quaternion deltaRotation;
            if (!TryComputeWorldAnchorDelta(driver, anchorPoint, currentGui, out deltaRotation))
            {
                return false;
            }

            float freeStepDegrees = ComputeFreeScreenDragDegrees(driver, guiDelta);
            if (freeStepDegrees > 0f)
            {
                float t = Mathf.InverseLerp(WorldFreeLimbReleaseCos, WorldFreeLimbDampStartCos, cosIncidence);
                float smoothT = t * t * (3f - 2f * t);
                float maxRimDegrees = freeStepDegrees * Mathf.Lerp(WorldFreeLimbOuterSpeedRatio, WorldFreeLimbInnerSpeedRatio, smoothT);
                float rotationAngle = Quaternion.Angle(Quaternion.identity, deltaRotation);
                if (rotationAngle > maxRimDegrees && rotationAngle > 0.000001f)
                {
                    deltaRotation = Quaternion.Slerp(Quaternion.identity, deltaRotation, maxRimDegrees / rotationAngle);
                }
            }

            return CommitWorldRotation(driver, driver.sphereRotation * deltaRotation);
        }

        private static void BeginWorldFreeLimbCoast(WorldCameraDriver driver, Vector2 exitGuiDelta)
        {
            worldPanAnchorValid = false;
            worldPanAnchorPoint = Vector3.zero;
            if (driver == null || PanInertiaStrength <= 0f
                || GetWorldPoleFactor(driver) >= WorldPoleInertiaDisable)
            {
                worldFreeLimbCoasting = false;
                worldPanVelocity = Vector2.zero;
                worldInertiaAnchorValid = false;
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (!GlobeTwist.HasTwisted && !WorldPanVelocityHistory.HasSamples && exitGuiDelta != Vector2.zero)
            {
                SampleWorldPanVelocity(exitGuiDelta);
            }

            Vector2 recentVelocity = GetRecentWorldPanVelocity(now);
            float stopSpeed = GetWorldPanInertiaStopSpeed(driver);
            if (!IsFinite(recentVelocity) || recentVelocity.sqrMagnitude <= stopSpeed * stopSpeed)
            {
                worldFreeLimbCoasting = false;
                worldPanVelocity = Vector2.zero;
                worldInertiaAnchorValid = false;
                return;
            }

            worldPanVelocity = recentVelocity;
            worldFreeLimbCoasting = true;
            worldInertiaAnchorValid = true;
        }

        private static void StepWorldFreeCoast(WorldCameraDriver driver, float dt)
        {
            if (!worldFreeLimbCoasting || driver == null || PanInertiaStrength <= 0f
                || GetWorldPoleFactor(driver) >= WorldPoleInertiaDisable)
            {
                worldFreeLimbCoasting = false;
                worldPanVelocity = Vector2.zero;
                worldInertiaAnchorValid = false;
                return;
            }

            float stopSpeed = GetWorldPanInertiaStopSpeed(driver);
            if (worldPanVelocity.sqrMagnitude <= stopSpeed * stopSpeed)
            {
                worldFreeLimbCoasting = false;
                worldPanVelocity = Vector2.zero;
                worldInertiaAnchorValid = false;
                return;
            }

            Vector2 inertiaGuiDelta = worldPanVelocity * PanInertiaStrength * WorldPanInertiaStrengthScale * GetWorldPanInertiaMoveScale(driver) * dt;
            if (!ApplyFreeScreenDrag(driver, inertiaGuiDelta))
            {
                worldFreeLimbCoasting = false;
                worldPanVelocity = Vector2.zero;
                worldInertiaAnchorValid = false;
                return;
            }

            worldPanVelocity *= Mathf.Exp(-GetWorldPanInertiaDamping(driver) * dt);
            if (worldPanVelocity.sqrMagnitude <= stopSpeed * stopSpeed)
            {
                worldFreeLimbCoasting = false;
                worldPanVelocity = Vector2.zero;
                worldInertiaAnchorValid = false;
            }
        }

        // Free-mode anchor correction in world space. The geographic point is
        // fixed: RimWorld orbits the camera around a stationary world sphere.
        private static bool TryComputeWorldAnchorDelta(WorldCameraDriver driver, Vector3 anchorPoint, Vector2 gui, out Quaternion deltaRotation)
        {
            deltaRotation = Quaternion.identity;
            if (driver == null)
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
            if (driver == null)
            {
                return false;
            }

            Quaternion deltaRotation;
            if (!TryComputeWorldAnchorDelta(driver, anchorPoint, gui, out deltaRotation))
            {
                return false;
            }

            Quaternion sphereRotation = driver.sphereRotation;

            // R maps world to view. The correction maps the fixed world anchor
            // onto the current world ray, so R' = R * deltaWorld. View-space
            // twist is left-multiplied instead.
            return CommitWorldRotation(driver, sphereRotation * deltaRotation);
        }

        private static bool CommitWorldRotation(WorldCameraDriver driver, Quaternion rotation)
        {
            double normSquared = (double)rotation.x * rotation.x + (double)rotation.y * rotation.y
                + (double)rotation.z * rotation.z + (double)rotation.w * rotation.w;
            if (driver == null || double.IsNaN(normSquared) || double.IsInfinity(normSquared) || normSquared < 0.000001)
            {
                return false;
            }
            double inverseNorm = 1.0 / Math.Sqrt(normSquared);
            driver.sphereRotation = new Quaternion((float)(rotation.x * inverseNorm), (float)(rotation.y * inverseNorm),
                (float)(rotation.z * inverseNorm), (float)(rotation.w * inverseNorm));
            // Touch takes over from jumps and the native north-up animation.
            ReflectionGuard.TrySetField(WorldRotationAnimationLerpField, driver, 1f);
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
            if (driver == null || Find.PlaySettings == null)
            {
                return;
            }
            if (Find.PlaySettings.lockNorthUp)
            {
                return;
            }
            Quaternion sphereRotation = driver.sphereRotation;
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
            if (driver == null || Find.PlaySettings == null)
            {
                northReturnActive = false;
                return;
            }
            if (Find.PlaySettings.lockNorthUp)
            {
                northReturnActive = false;
                return;
            }
            Quaternion sphereRotation = driver.sphereRotation;
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
            CommitWorldRotation(driver, next);
        }

        private static void CancelNorthReturn()
        {
            northReturnActive = false;
        }

        private const float MaxTurntableLatitudeDegrees = 85f;

        // Work entirely in camera-to-world coordinates C = inverse(sphereRotation).
        // C = yaw * pitch * roll: sequential world horizon pitch, then world north
        // yaw updates the first two factors while preserving the current roll.
        // There is no geographic anchor or per-frame sphere-raycast feedback.
        private static bool ApplyDualAxisTurntableDrag(WorldCameraDriver driver, Vector2 guiDelta)
        {
            if (driver == null || !IsFinite(guiDelta))
            {
                return false;
            }
            if (guiDelta.sqrMagnitude <= 0.00000001f)
            {
                return true;
            }
            Camera camera = GetWorldCamera(driver);
            if (camera == null || camera.pixelHeight <= 0 || Screen.width <= 0 || Screen.height <= 0 || UI.screenWidth <= 0 || UI.screenHeight <= 0)
            {
                return false;
            }

            Quaternion cameraRotation = Quaternion.Inverse(driver.sphereRotation);
            Vector3 forward = cameraRotation * Vector3.forward;
            Vector3 right = cameraRotation * Vector3.right;
            Vector3 up = cameraRotation * Vector3.up;
            Vector3 horizonRight = Vector3.Cross(Vector3.up, forward);
            if (horizonRight.sqrMagnitude <= 0.000001f)
            {
                // An external camera jump may put the view exactly on a pole.
                // Retain its horizontal basis rather than normalizing a zero cross.
                horizonRight = right;
                horizonRight.y = 0f;
            }
            if (horizonRight.sqrMagnitude <= 0.000001f || !IsFinite(forward))
            {
                return false;
            }
            horizonRight.Normalize();
            Vector3 meridianUp = Vector3.Cross(forward, horizonRight).normalized;

            // Project physical screen displacement into the current horizon.
            // A twisted globe still follows a rightward/downward finger motion.
            Vector3 dragOnScreen = right * (guiDelta.x * Screen.width / UI.screenWidth)
                - up * (guiDelta.y * Screen.height / UI.screenHeight);
            float eastPixels = Vector3.Dot(dragOnScreen, horizonRight);
            float southPixels = -Vector3.Dot(dragOnScreen, meridianUp);

            // Calibrate at the visible surface center, with no limb singularity.
            // Camera FOV is vertical; pixelHeight is the actual camera viewport.
            float degreesPerPixel;
            if (!TryGetWorldDegreesPerPixel(driver, out degreesPerPixel))
            {
                return false;
            }
            float sensitivity = RimTouchMod.Settings != null ? RimTouchMod.Settings.turntableDragSensitivity : 1f;
            if (float.IsNaN(sensitivity) || float.IsInfinity(sensitivity))
            {
                return false;
            }
            float scale = degreesPerPixel * Mathf.Clamp(sensitivity, 0.5f, 2f);
            if (RimTouchMod.Settings != null && RimTouchMod.Settings.invertTurntableAxis)
            {
                scale = -scale;
            }
            float latitude = Mathf.Asin(Mathf.Clamp(-forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float yawDegrees = eastPixels * scale / Mathf.Max(Mathf.Cos(latitude * Mathf.Deg2Rad), 0.25f);
            // Do not snap an externally positioned polar view back to 85 degrees.
            // Until it re-enters the normal range, only inward pitch is allowed.
            float targetLatitude = Mathf.Clamp(latitude + southPixels * scale,
                Mathf.Min(-MaxTurntableLatitudeDegrees, latitude), Mathf.Max(MaxTurntableLatitudeDegrees, latitude));
            float pitchDegrees = targetLatitude - latitude;
            if (float.IsNaN(yawDegrees) || float.IsInfinity(yawDegrees) || float.IsNaN(pitchDegrees))
            {
                return false;
            }
            cameraRotation = Quaternion.AngleAxis(pitchDegrees, horizonRight) * cameraRotation;
            cameraRotation = Quaternion.AngleAxis(yawDegrees % 360f, Vector3.up) * cameraRotation;
            // Rebuild the factorization using the CURRENT roll, not a gesture-start
            // pose. Double intermediates avoid accumulating float roll drift during
            // long circular drags, especially when the globe is turned sideways.
            // At or extremely close to a pole both roll components vanish, so keep
            // the sequential quaternion instead of letting Atan2 snap or amplify noise.
            Quaternion previous = driver.sphereRotation;
            double rollSin = 2.0 * ((double)previous.x * previous.y - (double)previous.z * previous.w);
            double rollCos = (double)previous.w * previous.w + (double)previous.y * previous.y
                - (double)previous.x * previous.x - (double)previous.z * previous.z;
            Vector3 nextForward = cameraRotation * Vector3.forward;
            double forwardHorizontalSquared = (double)nextForward.x * nextForward.x + (double)nextForward.z * nextForward.z;
            if (rollSin * rollSin + rollCos * rollCos > 0.00000001 && forwardHorizontalSquared > 0.00000001)
            {
                double roll = Math.Atan2(rollSin, rollCos);
                double yaw = Math.Atan2(nextForward.x, nextForward.z);
                double pitch = Math.Atan2(-nextForward.y, Math.Sqrt(forwardHorizontalSquared));
                double sy = Math.Sin(yaw * 0.5), cy = Math.Cos(yaw * 0.5);
                double sp = Math.Sin(pitch * 0.5), cp = Math.Cos(pitch * 0.5);
                double sr = Math.Sin(roll * 0.5), cr = Math.Cos(roll * 0.5);
                cameraRotation = new Quaternion((float)(cy * sp * cr + sy * cp * sr),
                    (float)(sy * cp * cr - cy * sp * sr), (float)(cy * cp * sr - sy * sp * cr),
                    (float)(cy * cp * cr + sy * sp * sr));
            }
            // Conjugation plus the commit's normalization is the inverse here;
            // avoid a second float normalization of the same unit quaternion.
            return CommitWorldRotation(driver, new Quaternion(-cameraRotation.x, -cameraRotation.y,
                -cameraRotation.z, cameraRotation.w));
        }

        private static bool TryGetWorldDegreesPerPixel(WorldCameraDriver driver, out float degreesPerPixel)
        {
            degreesPerPixel = 0f;
            Camera camera = GetWorldCamera(driver);
            if (camera == null || camera.pixelHeight <= 0 || Screen.width <= 0 || Screen.height <= 0 || UI.screenWidth <= 0 || UI.screenHeight <= 0)
            {
                return false;
            }

            float radius = GetWorldSphereRadius(driver);
            float distance = (camera.transform.position - GetWorldSphereCenter(driver)).magnitude;
            float surfaceDistance = Mathf.Max(distance - radius, 1f);
            float fovRadians = Mathf.Clamp(camera.fieldOfView, 5f, 170f) * Mathf.Deg2Rad;
            degreesPerPixel = 2f * surfaceDistance * Mathf.Tan(fovRadians * 0.5f)
                / (camera.pixelHeight * radius) * Mathf.Rad2Deg;
            return !float.IsNaN(degreesPerPixel) && !float.IsInfinity(degreesPerPixel) && degreesPerPixel > 0f;
        }

        private static float ComputeFreeScreenDragDegrees(WorldCameraDriver driver, Vector2 guiDelta)
        {
            float degreesPerPixel;
            if (!IsFinite(guiDelta) || guiDelta.sqrMagnitude <= 0.00000001f || !TryGetWorldDegreesPerPixel(driver, out degreesPerPixel))
            {
                return 0f;
            }

            float dxPixels = guiDelta.x * Screen.width / UI.screenWidth;
            float dyPixels = guiDelta.y * Screen.height / UI.screenHeight;
            return Mathf.Sqrt(dxPixels * dxPixels + dyPixels * dyPixels) * degreesPerPixel;
        }

        private static bool ApplyFreeScreenDrag(WorldCameraDriver driver, Vector2 guiDelta)
        {
            if (driver == null || !IsFinite(guiDelta))
            {
                return false;
            }
            if (guiDelta.sqrMagnitude <= 0.00000001f)
            {
                return true;
            }

            float degreesPerPixel;
            if (!TryGetWorldDegreesPerPixel(driver, out degreesPerPixel))
            {
                return false;
            }

            float yawDegrees = guiDelta.x * Screen.width / UI.screenWidth * degreesPerPixel;
            float pitchDegrees = guiDelta.y * Screen.height / UI.screenHeight * degreesPerPixel;
            float angleDegrees = Mathf.Sqrt(yawDegrees * yawDegrees + pitchDegrees * pitchDegrees);
            if (float.IsNaN(angleDegrees) || float.IsInfinity(angleDegrees) || angleDegrees <= 0.000001f)
            {
                return true;
            }

            // View-space axis perpendicular to the screen displacement. Left-multiplying
            // sphereRotation (R' = Inverse(deltaCam) * R) moves the visible surface
            // smoothly along the flick direction without a limb boundary.
            Vector3 viewAxis = new Vector3(pitchDegrees / angleDegrees, yawDegrees / angleDegrees, 0f);
            Quaternion next = Quaternion.AngleAxis(-angleDegrees, viewAxis) * driver.sphereRotation;
            return CommitWorldRotation(driver, next);
        }

        private static bool TryGetWorldSpherePointAtGui(Vector2 gui, out Vector3 point)
        {
            float unusedCosIncidence;
            return TryGetWorldSpherePointAtGui(gui, out point, out unusedCosIncidence);
        }

        private static bool TryGetWorldSpherePointAtGui(Vector2 gui, out Vector3 point, out float cosIncidence)
        {
            point = Vector3.zero;
            cosIncidence = 0f;
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
            if (!IsFinite(point))
            {
                return false;
            }

            cosIncidence = radius > 0.0001f ? Mathf.Clamp01(root / radius) : 0f;
            return true;
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
            if (driver == null)
            {
                return 0f;
            }

            Quaternion sphereRotation = driver.sphereRotation;

            Vector3 lookPoint = -(Quaternion.Inverse(sphereRotation) * Vector3.forward);
            if (!IsFinite(lookPoint) || lookPoint.sqrMagnitude <= 0.0001f)
            {
                return 0f;
            }
            return Mathf.Abs(lookPoint.normalized.y);
        }

        private static bool ShouldDisableWorldPanInertia(WorldCameraDriver driver)
        {
            return driver == null || !worldInertiaAnchorValid
                || (!LockNorthUpOnDragEnabled() && GetWorldPoleFactor(driver) >= WorldPoleInertiaDisable);
        }

        private static bool ShouldUseWorldAnchorPan()
        {
            // Camera orbit is available on both the landing-site page and in play.
            // Free mode alone needs a raycast anchor; dual-axis orbit does not.
            return IsWorldMapActive();
        }

        private static bool TryGetWorldAltitude(WorldCameraDriver driver, out float altitude)
        {
            altitude = driver != null ? driver.altitude : 0f;
            return driver != null;
        }

        private static bool TrySetWorldAltitude(WorldCameraDriver driver, float altitude)
        {
            if (driver == null || WorldCameraDesiredAltitudeField == null)
            {
                return false;
            }

            driver.altitude = altitude;
            bool desiredAltitudeSet = ReflectionGuard.TrySetField(WorldCameraDesiredAltitudeField, driver, altitude);
            return desiredAltitudeSet;
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
