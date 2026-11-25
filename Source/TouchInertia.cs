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
        private static readonly VelocityHistory PanVelocityHistory = new VelocityHistory(6, 0.14f);
        private static readonly VelocityHistory WorldPanVelocityHistory = new VelocityHistory(6, 0.14f);
        private static readonly VelocityHistory ZoomVelocityHistory = new VelocityHistory(6, 0.14f);

        private static void SamplePanVelocityFromMapDelta(Vector3 mapDelta)
        {
            float now = Time.realtimeSinceStartup;
            float dt = now - lastPanSampleTime;
            lastPanSampleTime = now;

            if (dt <= 0.0001f || dt > 0.18f)
            {
                return;
            }

            Vector3 velocity = mapDelta / dt;
            velocity.y = 0f;
            if (!IsFinite(velocity))
            {
                return;
            }

            if (velocity.magnitude > MaxPanInertiaSpeed)
            {
                velocity = velocity.normalized * MaxPanInertiaSpeed;
            }

            PanVelocityHistory.Record(velocity, now);
            panVelocity = GetRecentPanVelocity(now);
        }

        private static void SampleZoomVelocity(float previousValue, float currentValue)
        {
            float now = Time.realtimeSinceStartup;
            float dt = now - lastZoomSampleTime;
            lastZoomSampleTime = now;

            if (dt <= 0.0001f || dt > 0.18f || previousValue <= 0f || currentValue <= 0f)
            {
                return;
            }

            float velocity = Mathf.Log(currentValue / previousValue) / dt;
            if (float.IsNaN(velocity) || float.IsInfinity(velocity))
            {
                return;
            }

            velocity = Mathf.Clamp(velocity, -MaxZoomInertiaSpeed, MaxZoomInertiaSpeed);
            ZoomVelocityHistory.Record1D(velocity, now);
            zoomVelocity = GetRecentZoomVelocity(now);
        }

        private static void UpdateInertia()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Clamp(now - lastUpdateTime, 0.001f, 0.05f);
            lastUpdateTime = now;

            if (IsWorldMapActive())
            {
                UpdateWorldInertia(dt);
                return;
            }

            if (Current.ProgramState != ProgramState.Playing)
            {
                StopInertia();
                return;
            }

            if (Find.CurrentMap != null)
            {
                UpdateMapInertia(dt);
                return;
            }

            StopInertia();
        }

        private static void UpdateMapInertia(float dt)
        {
            CameraDriver cameraDriver = Find.CameraDriver;
            if (cameraDriver == null)
            {
                StopInertia();
                return;
            }

            worldPanVelocity = Vector2.zero;
            Vector3 rootPos;
            float size;
            if (!TryGetCameraRootPos(cameraDriver, out rootPos) || !TryGetCameraRootSize(cameraDriver, out size))
            {
                StopInertia();
                return;
            }

            bool changed = false;

            if (panVelocity.sqrMagnitude > MinPanInertiaSpeed * MinPanInertiaSpeed && PanInertiaStrength > 0f)
            {
                rootPos += panVelocity * PanInertiaStrength * dt;
                panVelocity *= Mathf.Exp(-PanInertiaDamping * dt);
                changed = true;
            }
            else
            {
                panVelocity = Vector3.zero;
            }

            if (Mathf.Abs(zoomVelocity) > MinZoomInertiaSpeed && ZoomInertiaStrength > 0f)
            {
                float targetSize = Mathf.Clamp(size * Mathf.Exp(zoomVelocity * ZoomInertiaStrength * dt), GetMinZoomSize(cameraDriver), GetMaxZoomSize(cameraDriver));
                if (Mathf.Abs(targetSize - size) > 0.001f)
                {
                    if (zoomInertiaAnchorValid)
                    {
                        cameraDriver.SetRootPosAndSize(rootPos, size);
                        Vector3 before = GuiToMapPosition(zoomInertiaGui);
                        cameraDriver.SetRootPosAndSize(rootPos, targetSize);
                        Vector3 after = GuiToMapPosition(zoomInertiaGui);
                        Vector3 zoomedRootPos;
                        if (!TryGetCameraRootPos(cameraDriver, out zoomedRootPos))
                        {
                            StopInertia();
                            return;
                        }
                        rootPos = zoomedRootPos + (before - after);
                    }
                    size = targetSize;
                    zoomVelocity *= Mathf.Exp(ZoomInertiaDamping * -dt);
                    changed = true;
                }
                else
                {
                    zoomVelocity = 0f;
                    zoomInertiaAnchorValid = false;
                }
            }
            else
            {
                zoomVelocity = 0f;
                zoomInertiaAnchorValid = false;
            }

            if (changed)
            {
                cameraDriver.SetRootPosAndSize(rootPos, size);
            }
        }

        private static void UpdateWorldInertia(float dt)
        {
            WorldCameraDriver driver = Find.WorldCameraDriver;
            if (driver == null)
            {
                StopInertia();
                return;
            }

            panVelocity = Vector3.zero;

            float worldPanStopSpeed = GetWorldPanInertiaStopSpeed(driver);
            if (worldPanVelocity.sqrMagnitude > worldPanStopSpeed * worldPanStopSpeed && PanInertiaStrength > 0f)
            {
                if (ShouldDisableWorldPanInertia(driver))
                {
                    worldPanVelocity = Vector2.zero;
                    worldInertiaAnchorValid = false;
                }
                else
                {
                    Vector2 inertiaGuiDelta = worldPanVelocity * PanInertiaStrength * WorldPanInertiaStrengthScale * GetWorldPanInertiaMoveScale(driver) * dt;
                    worldInertiaGui += inertiaGuiDelta;
                    // Continue the orbit smoothly without hitting a sphere-ray limb boundary:
                    // dual-axis uses the heading-preserving turntable; free mode uses view-space
                    // incremental rotation along the flick direction.
                    bool inertiaOk = LockNorthUpOnDragEnabled()
                        ? ApplyDualAxisTurntableDrag(driver, inertiaGuiDelta)
                        : ApplyFreeScreenDrag(driver, inertiaGuiDelta);
                    if (!inertiaOk)
                    {
                        worldPanVelocity = Vector2.zero;
                        worldInertiaAnchorValid = false;
                    }
                    else
                    {
                        worldPanVelocity *= Mathf.Exp(-GetWorldPanInertiaDamping(driver) * dt);
                        if (worldPanVelocity.sqrMagnitude <= worldPanStopSpeed * worldPanStopSpeed)
                        {
                            worldPanVelocity = Vector2.zero;
                            worldInertiaAnchorValid = false;
                        }
                    }
                }
            }
            else
            {
                worldPanVelocity = Vector2.zero;
                worldInertiaAnchorValid = false;
            }

            if (Mathf.Abs(zoomVelocity) > MinZoomInertiaSpeed && ZoomInertiaStrength > 0f)
            {
                float currentAltitude;
                if (!TryGetWorldAltitude(driver, out currentAltitude))
                {
                    zoomVelocity = 0f;
                    return;
                }

                float maxAltitude = GetWorldMaxAltitude(driver);
                float targetAltitude = Mathf.Clamp(currentAltitude * Mathf.Exp(zoomVelocity * ZoomInertiaStrength * dt), WorldCameraDriver.MinAltitude, maxAltitude);
                if (Mathf.Abs(targetAltitude - currentAltitude) > 0.001f)
                {
                    if (!TrySetWorldAltitude(driver, targetAltitude))
                    {
                        zoomVelocity = 0f;
                        return;
                    }
                    zoomVelocity *= Mathf.Exp(ZoomInertiaDamping * -dt);
                }
                else
                {
                    zoomVelocity = 0f;
                }
            }
            else
            {
                zoomVelocity = 0f;
            }
        }

        private static void FinalizeInertiaFromGesture()
        {
            if (mode == TouchMode.MapPan || mode == TouchMode.TwoFinger)
            {
                float now = Time.realtimeSinceStartup;
                zoomVelocity = GetRecentZoomVelocity(now);

                if (IsWorldMapActive())
                {
                    panVelocity = Vector3.zero;
                    WorldCameraDriver worldDriver = Find.WorldCameraDriver;
                    if (worldFreeLimbCoasting)
                    {
                        float stopSpeed = GetWorldPanInertiaStopSpeed(worldDriver);
                        if (worldPanVelocity.sqrMagnitude <= stopSpeed * stopSpeed
                            || ShouldDisableWorldPanInertia(worldDriver))
                        {
                            worldPanVelocity = Vector2.zero;
                            worldInertiaAnchorValid = false;
                        }
                        worldFreeLimbCoasting = false;
                    }
                    else
                    {
                        worldPanVelocity = GetRecentWorldPanVelocity(now);
                        if (panGestureDistance < DragSlopPixels * 1.25f
                            || worldPanVelocity.magnitude < GetWorldPanInertiaStartSpeed(worldDriver)
                            || ShouldDisableWorldPanInertia(worldDriver))
                        {
                            worldPanVelocity = Vector2.zero;
                            worldInertiaAnchorValid = false;
                        }
                    }
                }
                else
                {
                    worldPanVelocity = Vector2.zero;
                    panVelocity = GetRecentPanVelocity(now);

                    if (panGestureDistance < DragSlopPixels * 1.25f || panVelocity.magnitude < MinPanInertiaStartSpeed)
                    {
                        panVelocity = Vector3.zero;
                    }
                }

                if (Mathf.Abs(zoomVelocity) < MinZoomInertiaStartSpeed)
                {
                    zoomVelocity = 0f;
                    zoomInertiaAnchorValid = false;
                }
            }
            else
            {
                StopInertia();
            }
        }

        private static void StopInertia()
        {
            panVelocity = Vector3.zero;
            worldPanVelocity = Vector2.zero;
            pendingWorldCameraDolly = Vector2.zero;
            worldInertiaAnchorValid = false;
            worldInertiaAnchorPoint = Vector3.zero;
            worldInertiaGui = Vector2.zero;
            worldFreeLimbCoasting = false;
            zoomInertiaGui = Vector2.zero;
            zoomInertiaAnchorValid = false;
            zoomVelocity = 0f;
        }

        private static void ClearInertiaSamples()
        {
            PanVelocityHistory.Clear();
            WorldPanVelocityHistory.Clear();
            ZoomVelocityHistory.Clear();
        }

        private static Vector3 GetRecentPanVelocity(float now)
        {
            return PanVelocityHistory.HasSamples ? PanVelocityHistory.GetWeightedAverage(now) : panVelocity;
        }

        private static void SampleWorldPanVelocity(Vector2 guiDelta)
        {
            float now = Time.realtimeSinceStartup;
            float dt = now - lastWorldPanSampleTime;
            lastWorldPanSampleTime = now;

            if (dt <= 0.0001f || dt > 0.18f || guiDelta == Vector2.zero)
            {
                return;
            }

            Vector2 velocity = guiDelta / dt;
            if (!IsFinite(velocity))
            {
                return;
            }

            if (velocity.magnitude > MaxWorldPanInertiaSpeed)
            {
                velocity = velocity.normalized * MaxWorldPanInertiaSpeed;
            }

            WorldPanVelocityHistory.Record2D(velocity, now);
            worldPanVelocity = GetRecentWorldPanVelocity(now);
        }

        private static Vector2 GetRecentWorldPanVelocity(float now)
        {
            return WorldPanVelocityHistory.HasSamples ? WorldPanVelocityHistory.GetWeightedAverage2D(now) : worldPanVelocity;
        }

        private static float GetRecentZoomVelocity(float now)
        {
            return ZoomVelocityHistory.HasSamples ? ZoomVelocityHistory.GetWeightedAverage1D(now) : zoomVelocity;
        }

        private static bool IsFinite(Vector3 vector)
        {
            return !float.IsNaN(vector.x) && !float.IsInfinity(vector.x)
                && !float.IsNaN(vector.y) && !float.IsInfinity(vector.y)
                && !float.IsNaN(vector.z) && !float.IsInfinity(vector.z);
        }

        private static bool IsFinite(Vector2 vector)
        {
            return !float.IsNaN(vector.x) && !float.IsInfinity(vector.x)
                && !float.IsNaN(vector.y) && !float.IsInfinity(vector.y);
        }

        private static float PanInertiaStrength
        {
            get
            {
                return RimTouchMod.Settings != null ? RimTouchMod.Settings.panInertiaStrength : DefaultPanInertiaStrength;
            }
        }

        private static float PanInertiaDamping
        {
            get
            {
                return RimTouchMod.Settings != null ? Mathf.Max(0.1f, RimTouchMod.Settings.panInertiaDamping) : DefaultPanInertiaDamping;
            }
        }

        private static float ZoomInertiaStrength
        {
            get
            {
                return RimTouchMod.Settings != null ? RimTouchMod.Settings.zoomInertiaStrength : DefaultZoomInertiaStrength;
            }
        }

        private static float ZoomInertiaDamping
        {
            get
            {
                return RimTouchMod.Settings != null ? Mathf.Max(0.1f, RimTouchMod.Settings.zoomInertiaDamping) : DefaultZoomInertiaDamping;
            }
        }
    }
}
