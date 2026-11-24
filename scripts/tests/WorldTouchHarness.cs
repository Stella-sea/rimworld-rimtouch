// Test-only services. The globe is stationary; C = inverse(sphereRotation).
// System.Numerics supplies single-precision rotation math independently of the
// production turntable. Direction/roll oracles use actual projected world points.
#pragma warning disable 0169, 0414, 0649
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using RimTouch.TouchPlatform;

namespace UnityEngine
{
    public struct Rect { }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2();
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector2 normalized => this / magnitude;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float b) => new Vector2(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, float b) => new Vector2(a.x / b, a.y / b);
        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object o) => o is Vector2 v && this == v;
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3();
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public float sqrMagnitude => Dot(this, this);
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 1e-12f ? this / magnitude : zero;
        public void Normalize() { this = normalized; }
        internal System.Numerics.Vector3 Numeric => new System.Numerics.Vector3(x, y, z);
        internal static Vector3 From(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => a + -b;
        public static Vector3 operator -(Vector3 a) => a * -1;
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator /(Vector3 a, float b) => a * (1 / b);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => From(System.Numerics.Vector3.Cross(a.Numeric, b.Numeric));
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        internal System.Numerics.Quaternion Numeric => new System.Numerics.Quaternion(x, y, z, w);
        internal static Quaternion From(System.Numerics.Quaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion operator *(Quaternion a, Quaternion b) => From(a.Numeric * b.Numeric);
        public static Vector3 operator *(Quaternion a, Vector3 b) => Vector3.From(System.Numerics.Vector3.Transform(b.Numeric, a.Numeric));
        public static Quaternion Inverse(Quaternion q) => From(System.Numerics.Quaternion.Inverse(q.Numeric));
        public static Quaternion AngleAxis(float degrees, Vector3 axis) => From(System.Numerics.Quaternion.CreateFromAxisAngle(axis.normalized.Numeric, degrees * Mathf.Deg2Rad));
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => From(System.Numerics.Quaternion.Slerp(a.Numeric, b.Numeric, t));
        public static float Angle(Quaternion a, Quaternion b)
        {
            float dot = System.Numerics.Quaternion.Dot(System.Numerics.Quaternion.Normalize(a.Numeric), System.Numerics.Quaternion.Normalize(b.Numeric));
            return 2 * (float)Math.Acos(Math.Clamp(Math.Abs(dot), 0f, 1f)) * Mathf.Rad2Deg;
        }
        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            from.Normalize(); to.Normalize();
            var v = Vector3.Cross(from, to);
            float w = 1 + Vector3.Dot(from, to);
            if (w < 1e-6f) return AngleAxis(180, Vector3.Cross(from, Math.Abs(from.x) < .8f ? Vector3.right : Vector3.up));
            return From(System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion(v.x, v.y, v.z, w)));
        }
        public static Quaternion LookRotation(Vector3 forward)
        {
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var up = Vector3.Cross(forward, right);
            return From(System.Numerics.Quaternion.CreateFromRotationMatrix(new System.Numerics.Matrix4x4(
                right.x, right.y, right.z, 0, up.x, up.y, up.z, 0,
                forward.x, forward.y, forward.z, 0, 0, 0, 0, 1)));
        }
    }
    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180), Rad2Deg = (float)(180 / Math.PI);
        public static float Clamp(float v, float lo, float hi) => Math.Clamp(v, lo, hi);
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Max(float a, float b, float c) => Max(a, Max(b, c));
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Abs(float v) => Math.Abs(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Tan(float v) => (float)Math.Tan(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Asin(float v) => (float)Math.Asin(v);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Log(float v) => (float)Math.Log(v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => Clamp01((v - a) / (b - a));
    }
    public static class Time { public static float realtimeSinceStartup, unscaledDeltaTime = 1f / 60; }
    public static class Screen { public static int width = 1600, height = 900; }
    public class Transform { public Quaternion rotation = Quaternion.identity; public Vector3 position; }
    public struct Ray { public Vector3 origin, direction; }
    public struct RaycastHit { }
    public class Camera
    {
        public Transform transform = new Transform();
        public float fieldOfView = 40;
        public int pixelHeight => Screen.height;
        public static int Raycasts;
        public Ray ScreenPointToRay(Vector3 p)
        {
            Raycasts++;
            float focal = pixelHeight / (2 * Mathf.Tan(fieldOfView * Mathf.Deg2Rad / 2));
            return new Ray { origin = transform.position, direction = transform.rotation
                * new Vector3((p.x - Screen.width / 2f) / focal, (p.y - Screen.height / 2f) / focal, 1).normalized };
        }
        public Vector2 ProjectGui(Vector3 world)
        {
            Vector3 p = Quaternion.Inverse(transform.rotation) * (world - transform.position);
            float focal = pixelHeight / (2 * Mathf.Tan(fieldOfView * Mathf.Deg2Rad / 2));
            return new Vector2((Screen.width / 2f + p.x * focal / p.z) * UI.screenWidth / Screen.width,
                (Screen.height / 2f - p.y * focal / p.z) * UI.screenHeight / Screen.height);
        }
    }
    public static class Physics
    {
        public static bool Raycast(Ray ray, out RaycastHit hit, float distance, int mask)
        {
            hit = new RaycastHit();
            var d = Find.WorldCameraDriver;
            Vector3 origin = ray.origin - d.layerOriginOffset;
            float b = Vector3.Dot(origin, ray.direction);
            return b * b - origin.sqrMagnitude + WorldCameraDriver.SphereRadius * WorldCameraDriver.SphereRadius >= 0;
        }
    }
}
namespace HarmonyLib { }
namespace Verse
{
    public enum ProgramState { Entry, Playing }
    public static class Current { public static ProgramState ProgramState = ProgramState.Playing; }
    public class Map { }
    public static class UI { public static int screenWidth = 1600, screenHeight = 900; }
    public class CameraDriver { public void SetRootPosAndSize(Vector3 p, float s) { } }
    public static class Find
    {
        public static WorldCameraDriver WorldCameraDriver;
        public static WorldSelector WorldSelector = new WorldSelector();
        public static PlaySettings PlaySettings = new PlaySettings();
        public static World World = new World();
        public static Map CurrentMap;
        public static CameraDriver CameraDriver;
    }
}
namespace RimWorld { public class PlaySettings { public bool lockNorthUp; } }
namespace RimWorld.Planet
{
    public struct PlanetTile { public bool Valid; public static PlanetTile Invalid => new PlanetTile(); }
    public class WorldObject { }
    public class WorldRenderer { public PlanetTile GetTileFromRayHit(RaycastHit h) => new PlanetTile { Valid = true }; }
    public class World { public WorldRenderer renderer = new WorldRenderer(); }
    public static class WorldRendererUtility { public static bool WorldRendered = true; }
    public static class WorldCameraManager { public const float FarClipPlane = 1000; public const int WorldLayerMask = 1; }
    public static class GenWorldUI { public static List<WorldObject> WorldObjectsUnderMouse(Vector2 p) => new List<WorldObject>(); }
    public class WorldSelector
    {
        public int Clears, Selects;
        public bool HasSelection = true;
        public void ClearSelection() { Clears++; HasSelection = false; }
        public void SelectFirstOrNextAt(PlanetTile t) { Selects++; HasSelection = true; }
        public void Select(WorldObject o, bool add) { Selects++; HasSelection = true; }
    }
    public class WorldCameraDriver
    {
        public static float SphereRadius = 100, MaxAltitude = 400;
        public const float MinAltitude = 101;
        public Quaternion sphereRotation = Quaternion.identity, rotationAnimation_prevSphereRotation = Quaternion.identity;
        public float altitude = 200, desiredAltitude = 200, rotationAnimation_lerpFactor = 1, mouseTouchingScreenBottomEdgeStartTime;
        public Vector2 rotationVelocity;
        public Vector3 layerOriginOffset;
        public Camera cachedCamera = new Camera();
        public int Applies;
        public float AltitudePercent => (altitude - MinAltitude) / (MaxAltitude - MinAltitude);
        public void ApplyPositionToGameObject()
        {
            Applies++;
            var r = rotationAnimation_lerpFactor < 1 ? Quaternion.Slerp(rotationAnimation_prevSphereRotation, sphereRotation, rotationAnimation_lerpFactor) : sphereRotation;
            cachedCamera.transform.rotation = Quaternion.Inverse(r);
            cachedCamera.transform.position = layerOriginOffset - cachedCamera.transform.rotation * Vector3.forward * altitude;
        }
    }
}
namespace RimTouch.TouchPlatform
{
    public enum ContactPhase { Began, Moved, Stationary, Ended }
    public struct ContactTouch { public long fingerId; public ContactPhase phase; public Vector2 position; }
    public static class TouchInputProof { public static void RecordTwist(float d) { } }
}
namespace RimTouch
{
    public class TestSettings
    {
        public bool lockNorthUpOnDrag = true, invertTurntableAxis, autoReturnNorth, enableGlobeTwist = true;
        public float turntableDragSensitivity = 1, northReturnDuration = .8f, twistCommitDegrees = 10;
        public float panInertiaStrength = 1.05f, panInertiaDamping = 4.8f, zoomInertiaStrength = 1.05f, zoomInertiaDamping = 4.8f;
    }
    public static class RimTouchMod { public static TestSettings Settings = new TestSettings(); }
    public static class TouchTapRepair { public static void Clear() { } }
    internal static class ReflectionGuard
    {
        public static bool TryGetField<T>(FieldInfo f, object o, out T v) { v = f == null ? default(T) : (T)f.GetValue(o); return f != null; }
        public static bool TrySetField(FieldInfo f, object o, object v) { if (f == null) return false; f.SetValue(o, v); return true; }
        public static bool TryInvoke(MethodInfo m, object o, object[] args) { m.Invoke(o, args); return true; }
    }
    public static partial class TouchInputDriver
    {
        static FieldInfo Field(string n) => typeof(WorldCameraDriver).GetField(n);
        static readonly FieldInfo WorldCameraRotationVelocityField = Field("rotationVelocity"),
            WorldCameraMouseBottomEdgeStartField = Field("mouseTouchingScreenBottomEdgeStartTime"),
            WorldCameraDesiredAltitudeField = Field("desiredAltitude"), WorldCameraMaxAltitudeField = Field("MaxAltitude"),
            WorldRotationAnimationLerpField = Field("rotationAnimation_lerpFactor"), WorldCameraSphereRadiusField = Field("SphereRadius"),
            WorldCameraLayerOriginOffsetField = Field("layerOriginOffset"), WorldCameraCachedCameraField = Field("cachedCamera");
        static readonly MethodInfo WorldCameraApplyPositionMethod = typeof(WorldCameraDriver).GetMethod("ApplyPositionToGameObject");
        static bool TouchModeEnabled = true, ShouldSuppressWorldEdgeScroll;
        static bool blockingWindow;
        static int queuedUiTaps, worldBoxes;
        static bool IsEnded(ContactTouch t) => t.phase == ContactPhase.Ended;
        static bool IsBlockingWindowAt(Vector2 p) => blockingWindow;
        static bool IsVanillaMapToolActive() => false;
        static void SuppressUiClickFallout() { }
        static void CancelRimWorldDragBox() { }
        static void CancelWorldDragBox() { }
        static void BeginWorldDragBox(Vector2 p) { worldBoxes++; }
        static void FinishRimWorldDragBox() { }
        static void FinishWorldDragBox() { }
        static void FinalizeUiScrollInertia() { }
        static void QueueUiTap(Vector2 p) { queuedUiTaps++; }
        static bool TryCancelVanillaMapToolFromTwoFingerTap() => false;
        static bool TrySelectFromTwoFingerDraftedTap() => false;
        static bool TryDoDraftedPawnTapCommand(Vector2 p) => false;
        static void DoMapLeftClick(Vector2 p) { }
        static void FinishTouch() { mode = TouchMode.None; worldPanAnchorValid = false; }
        static bool TryGetCameraRootPos(CameraDriver d, out Vector3 p) { p = Vector3.zero; return true; }
        static bool TryGetCameraRootSize(CameraDriver d, out float s) { s = 10; return true; }
        static float GetMinZoomSize(CameraDriver d) => 1;
        static float GetMaxZoomSize(CameraDriver d) => 100;
        static Vector3 GuiToMapPosition(Vector2 p) => Vector3.zero;
        static Vector3 KeepMapPointUnderGui(Vector3 p, Vector2 gui) => Vector3.zero;
        static void ApplyPinchZoom(float a, float b, Vector2 p) { }
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        static double Heading(Quaternion r)
        {
            Vector3 n = r * Vector3.up;
            return Math.Atan2(n.x, n.y) * 180 / Math.PI;
        }
        static double Difference(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 360));
        static double Latitude(Quaternion r) => Math.Asin(Math.Clamp(-(Quaternion.Inverse(r) * Vector3.forward).y, -1, 1)) * 180 / Math.PI;
        static WorldCameraDriver Setup(float longitude = 0, float latitude = 0, float roll = 0)
        {
            var c = Quaternion.AngleAxis(longitude, Vector3.up) * Quaternion.AngleAxis(latitude, Vector3.right) * Quaternion.AngleAxis(roll, Vector3.forward);
            var d = new WorldCameraDriver { sphereRotation = Quaternion.Inverse(c) };
            Find.WorldCameraDriver = d;
            Find.PlaySettings.lockNorthUp = false;
            Find.WorldSelector = new WorldSelector();
            WorldRendererUtility.WorldRendered = true;
            RimTouchMod.Settings = new TestSettings();
            Screen.width = UI.screenWidth = 1600; Screen.height = UI.screenHeight = 900;
            StopInertia(); ClearInertiaSamples(); CancelNorthReturn();
            mode = TouchMode.None; worldPanAnchorValid = false; uiScrollGestureActive = false;
            startedOverUi = suppressReleaseTap = rightClickDone = worldDoubleTapPrimed = blockingWindow = false;
            queuedUiTaps = worldBoxes = 0;
            d.ApplyPositionToGameObject();
            return d;
        }
        static ContactTouch Contact(Vector2 gui, ContactPhase phase = ContactPhase.Moved)
        {
            return new ContactTouch { fingerId = 1, phase = phase,
                position = new Vector2(gui.x * Screen.width / UI.screenWidth, (UI.screenHeight - gui.y) * Screen.height / UI.screenHeight) };
        }
        static void Begin(Vector2 p)
        {
            mode = TouchMode.OneFinger; startGui = lastGui = p; startTime = Time.realtimeSinceStartup;
            panGestureDistance = 0; worldPanAnchorValid = false; suppressReleaseTap = false;
        }

        public static void RunWorldTouchTests()
        {
            double maxDrift = 0;
            int poses = 0;
            foreach (float lon in new[] { 0f, 90f, 180f, 270f })
            foreach (float lat in new[] { -75f, 0f, 45f, 75f })
            foreach (float roll in new[] { 0f, 37f, 90f, 180f })
            {
                var d = Setup(lon, lat, roll);
                double initial = Heading(d.sphereRotation);
                foreach (var delta in new[] { new Vector2(.25f, 0), new Vector2(0, .25f), new Vector2(.2f, .15f) })
                {
                    d = Setup(lon, lat, roll);
                    Vector3 centerPoint = d.layerOriginOffset - (Quaternion.Inverse(d.sphereRotation) * Vector3.forward) * WorldCameraDriver.SphereRadius;
                    Vector2 before = d.cachedCamera.ProjectGui(centerPoint);
                    Assert(ApplyDualAxisTurntableDrag(d, delta), "orbit rejected a valid pose");
                    Vector2 moved = d.cachedCamera.ProjectGui(centerPoint) - before;
                    Assert(moved.x * delta.x + moved.y * delta.y > 0, $"screen drag reversed at {lon}/{lat}/{roll}");
                    if (delta.y == 0) Assert(Math.Abs(moved.y) < Math.Abs(moved.x) * .01 + .001, "rightward drag was not screen-relative");
                    if (delta.x == 0) Assert(Math.Abs(moved.x) < Math.Abs(moved.y) * .01 + .001, "downward drag was not screen-relative");
                    Assert(Difference(Heading(d.sphereRotation), initial) < .001, "single-step roll drift");
                }
                d = Setup(lon, lat, roll);
                for (int i = 0; i < 3600; i++)
                {
                    double theta = i * 2 * Math.PI / 360;
                    Assert(ApplyDualAxisTurntableDrag(d, new Vector2((float)Math.Cos(theta), (float)Math.Sin(theta))), "circle update failed");
                    maxDrift = Math.Max(maxDrift, Difference(Heading(d.sphereRotation), initial));
                }
                Assert(Difference(Heading(d.sphereRotation), initial) < .001, $"circle roll drift {maxDrift} at {lon}/{lat}/{roll}");
                var stopped = d.sphereRotation;
                int applies = d.Applies;
                for (int i = 0; i < 60; i++) ApplyDualAxisTurntableDrag(d, Vector2.zero);
                Assert(d.Applies == applies && Difference(Heading(d.sphereRotation), Heading(stopped)) == 0, "stationary input moved the view");
                poses++;
            }
            Console.WriteLine($"PASS {poses} poses: screen-relative directions, ten circles, stationary input; maximum heading drift {maxDrift:F6} degrees");

            // Pure view roll, including an off-center midpoint and subsequent drag.
            foreach (float roll in new[] { 0f, 37f, 90f, 180f })
            {
                var d = Setup(90, 45, roll);
                var oldForward = Quaternion.Inverse(d.sphereRotation) * Vector3.forward;
                double h = Heading(d.sphereRotation);
                int rays = Camera.Raycasts;
                Assert(ApplyWorldTwist(40, new Vector2(950, 400)), "twist failed");
                Assert((Quaternion.Inverse(d.sphereRotation) * Vector3.forward - oldForward).magnitude < 1e-5, "twist changed view direction");
                Assert(Difference(Heading(d.sphereRotation), h + 40) < .001, "twist heading sign/amount");
                Assert(Camera.Raycasts == rays, "dual-axis twist still used ray re-anchoring");
                h = Heading(d.sphereRotation);
                for (int i = 0; i < 50; i++) ApplyDualAxisTurntableDrag(d, new Vector2(1, 1));
                Assert(Difference(Heading(d.sphereRotation), h) < .001, "drag lost twisted heading");
            }
            Console.WriteLine("PASS twist changes only view roll, then drag preserves the new heading");

            var driver = Setup();
            ApplyDualAxisTurntableDrag(driver, new Vector2(0, 100000));
            Assert(Math.Abs(Latitude(driver.sphereRotation) - 85) < .001, "north pole limit");
            ApplyDualAxisTurntableDrag(driver, new Vector2(0, 100000));
            Assert(Math.Abs(Latitude(driver.sphereRotation) - 85) < .001, "north pole repeated input");
            ApplyDualAxisTurntableDrag(driver, new Vector2(0, -100000));
            Assert(Math.Abs(Latitude(driver.sphereRotation) + 85) < .001, "south pole limit");
            driver = Setup(50, 89);
            double externalLat = Latitude(driver.sphereRotation);
            ApplyDualAxisTurntableDrag(driver, new Vector2(.2f, 0));
            Assert(Math.Abs(Latitude(driver.sphereRotation) - externalLat) < .001, "external polar pose snapped on horizontal drag");
            driver = Setup(0, 89.99995f, 45);
            var nearPoleBefore = driver.sphereRotation;
            Assert(ApplyDualAxisTurntableDrag(driver, new Vector2(0, -10)), "near-pole inward drag failed");
            Assert(Quaternion.Angle(nearPoleBefore, driver.sphereRotation) < 10f, "near-pole inward drag snapped roll");
            Assert(!ApplyDualAxisTurntableDrag(driver, new Vector2(float.NaN, 0)), "NaN touch accepted");
            driver = Setup(); Find.PlaySettings.lockNorthUp = true;
            Assert(!ApplyWorldTwist(float.NaN, new Vector2(800, 450)) && Find.PlaySettings.lockNorthUp, "invalid twist cleared north lock");
            Console.WriteLine("PASS polar limits, external polar pose, invalid input");

            // Same physical drag with different UI scale, plus sensitivity/inversion.
            driver = Setup(90, 45, 37);
            ApplyDualAxisTurntableDrag(driver, new Vector2(10, 10));
            var expected = driver.sphereRotation;
            driver = Setup(90, 45, 37); UI.screenWidth = 800; UI.screenHeight = 450;
            ApplyDualAxisTurntableDrag(driver, new Vector2(5, 5));
            Assert((expected * Vector3.forward - driver.sphereRotation * Vector3.forward).magnitude < 1e-5, "UI scale changed physical drag");
            driver = Setup(); RimTouchMod.Settings.turntableDragSensitivity = 2;
            ApplyDualAxisTurntableDrag(driver, new Vector2(5, 0));
            var fast = driver.sphereRotation;
            driver = Setup(); ApplyDualAxisTurntableDrag(driver, new Vector2(10, 0));
            Assert((fast * Vector3.forward - driver.sphereRotation * Vector3.forward).magnitude < 1e-5, "sensitivity not applied to displacement");
            driver = Setup(); RimTouchMod.Settings.invertTurntableAxis = true;
            Vector3 p = new Vector3(0, 0, -100);
            Vector2 screenBefore = driver.cachedCamera.ProjectGui(p);
            ApplyDualAxisTurntableDrag(driver, new Vector2(10, 0));
            Assert(driver.cachedCamera.ProjectGui(p).x < screenBefore.x, "inversion did not reverse screen motion");
            Console.WriteLine("PASS UI scale, sensitivity, inversion");

            foreach (int samples in new[] { 30, 60, 120 })
            {
                driver = Setup(90, 45, 37);
                for (int i = 0; i < samples; i++) ApplyDualAxisTurntableDrag(driver, new Vector2(12f / samples, 0));
                Assert(Difference(Heading(driver.sphereRotation), 37) < .001, "sample rate changed heading");
            }
            // Incremental orbit depends on displacement, not on how long the
            // finger was held or where the screen-space gesture began.
            driver = Setup(); Begin(new Vector2(800, 450));
            Time.realtimeSinceStartup += .02f;
            UpdateOneFingerWorld(Contact(new Vector2(840, 450)), new Vector2(840, 450));
            var fromGlobe = driver.sphereRotation;
            driver = Setup(); Begin(new Vector2(10, 10));
            Time.realtimeSinceStartup += .02f;
            UpdateOneFingerWorld(Contact(new Vector2(50, 10)), new Vector2(50, 10));
            Assert((fromGlobe * Vector3.forward - driver.sphereRotation * Vector3.forward).magnitude < 1e-5, "inside/outside drag changed solver");
            int heldApplies = driver.Applies;
            Time.realtimeSinceStartup += .2f;
            UpdateOneFingerWorld(Contact(lastGui, ContactPhase.Stationary), lastGui);
            Assert(driver.Applies == heldApplies, "held outside pointer caused feedback rotation");
            Time.realtimeSinceStartup += .02f;
            UpdateOneFingerWorld(Contact(lastGui, ContactPhase.Ended), lastGui);
            Assert(worldPanVelocity == Vector2.zero && Find.WorldSelector.HasSelection, "held release started stale inertia or cleared selection");
            Console.WriteLine("PASS sampling rates; same inside/outside orbit; held outside pointer/release");

            // Selection behavior calls real UpdateOneFingerWorld/CompleteActiveGesture.
            Vector2 outside = new Vector2(10, 10);
            driver = Setup(); Find.PlaySettings.lockNorthUp = true;
            Begin(outside);
            UpdateOneFingerWorld(Contact(outside, ContactPhase.Ended), outside);
            Assert(Find.WorldSelector.Clears == 1 && !Find.WorldSelector.HasSelection, "outside tap did not clear selection exactly once");
            Assert(driver.Applies == 1 && Find.PlaySettings.lockNorthUp, "outside tap changed camera/north lock");
            driver = Setup(); Begin(outside);
            var jitter = outside + new Vector2(3, 2);
            UpdateOneFingerWorld(Contact(jitter, ContactPhase.Ended), jitter);
            Assert(Find.WorldSelector.Clears == 1 && driver.Applies == 1, "tap jitter became orbit");
            driver = Setup(); Find.PlaySettings.lockNorthUp = true; Begin(outside);
            int rayCount = Camera.Raycasts;
            var dragged = outside + new Vector2(40, 30);
            Time.realtimeSinceStartup += .02f;
            UpdateOneFingerWorld(Contact(dragged), dragged);
            Assert(mode == TouchMode.MapPan && driver.Applies > 1 && !Find.PlaySettings.lockNorthUp, "outside drag did not take over");
            Assert(Camera.Raycasts == rayCount && pendingWorldCameraDolly == Vector2.zero, "outside drag used anchor/dolly fallback");
            Time.realtimeSinceStartup += .02f;
            UpdateOneFingerWorld(Contact(outside, ContactPhase.Ended), outside);
            Assert(Find.WorldSelector.Clears == 0 && Find.WorldSelector.HasSelection, "drag returning to start cleared selection");
            driver = Setup(); Begin(outside); suppressReleaseTap = true;
            CompleteActiveGesture(outside);
            Assert(Find.WorldSelector.Clears == 0, "suppressed release cleared selection");
            driver = Setup(); Begin(new Vector2(800, 450));
            UpdateOneFingerWorld(Contact(startGui, ContactPhase.Ended), startGui);
            Assert(Find.WorldSelector.Selects == 1 && Find.WorldSelector.Clears == 0, "globe tap no longer selects");
            driver = Setup(); Begin(outside); startedOverUi = true;
            CompleteActiveGesture(outside);
            Assert(queuedUiTaps == 1 && Find.WorldSelector.Clears == 0 && driver.Applies == 1, "UI tap leaked to world selection");
            driver = Setup(); Begin(outside); worldDoubleTapPrimed = true;
            UpdateOneFingerWorld(Contact(dragged), dragged);
            Assert(mode == TouchMode.WorldDragBox && worldBoxes == 1 && driver.Applies == 1, "double-tap drag-box regressed");
            Console.WriteLine("PASS outside tap/jitter clears once; outside drag/return/suppressed release retains selection; globe/UI taps and drag-box preserved");

            // Exercise actual inertia and two-contact dispatch, not a parallel model.
            driver = Setup(90, 75, 37); worldInertiaAnchorValid = true; worldPanVelocity = new Vector2(600, 100);
            double beforeInertia = Heading(driver.sphereRotation);
            UpdateWorldInertia(.016f);
            Assert(driver.Applies > 1 && Difference(Heading(driver.sphereRotation), beforeInertia) < .001, "inertia lost orbit heading");
            driver = Setup(); blockingWindow = true;
            UpdateTwoFinger(Contact(new Vector2(400, 400)), Contact(new Vector2(600, 400)));
            Assert(startedOverUi && driver.Applies == 1, "two fingers on a window moved globe");
            driver = Setup(180, 45, 37);
            UpdateTwoFinger(Contact(new Vector2(400, 400)), Contact(new Vector2(600, 400)));
            Time.realtimeSinceStartup += .02f;
            double h2 = Heading(driver.sphereRotation);
            UpdateTwoFinger(Contact(new Vector2(420, 410)), Contact(new Vector2(620, 410)));
            Assert(driver.Applies > 1 && Difference(Heading(driver.sphereRotation), h2) < .001, "two-finger pan lost heading");
            Find.PlaySettings.lockNorthUp = true;
            UpdateTwoFinger(Contact(new Vector2(420, 410)), Contact(new Vector2(620, 410)));
            Assert(Find.PlaySettings.lockNorthUp, "stationary two-finger update overrode north button");
            driver.sphereRotation = ComputeNorthUpRotation(driver.sphereRotation); driver.ApplyPositionToGameObject();
            Time.realtimeSinceStartup += .02f;
            UpdateTwoFinger(Contact(new Vector2(425, 410)), Contact(new Vector2(625, 410)));
            Assert(Difference(Heading(driver.sphereRotation), 0) < .001, "drag did not use external north reset");
            // Asymmetric release after a committed twist must not launch pan inertia.
            driver = Setup(90, 45, 0);
            UpdateTwoFinger(Contact(new Vector2(400, 450)), Contact(new Vector2(600, 450)));
            Time.realtimeSinceStartup += .02f;
            UpdateTwoFinger(Contact(new Vector2(420, 390)), Contact(new Vector2(580, 510)));
            Assert(GlobeTwist.HasTwisted, "twist did not commit");
            Time.realtimeSinceStartup += .016f;
            UpdateTwoFinger(Contact(new Vector2(450, 390), ContactPhase.Ended), Contact(new Vector2(610, 510)));
            Assert(worldPanVelocity == Vector2.zero, "twist release launched pan inertia");
            Console.WriteLine("PASS actual inertia and two-finger pan; UI window exclusion; native north reset; twist release inertia suppression");

            // Native animation must be stopped by either kind of direct orbit.
            driver = Setup(90, 45, 37); driver.rotationAnimation_lerpFactor = .1f;
            ApplyDualAxisTurntableDrag(driver, new Vector2(1, 1));
            Assert(driver.rotationAnimation_lerpFactor == 1, "dual-axis left native animation running");

            double maxAnchorError = 0;
            foreach (float lon in new[] { 0f, 90f, 180f, 270f })
            foreach (float lat in new[] { -45f, 0f, 45f })
            {
                driver = Setup(lon, lat, 37); RimTouchMod.Settings.lockNorthUpOnDrag = false;
                Vector2 source = new Vector2(800, 450), target = new Vector2(808, 454);
                Assert(TryGetWorldSpherePointAtGui(source, out Vector3 anchor), "free anchor initial ray missed");
                driver.rotationAnimation_lerpFactor = .1f;
                for (int i = 0; i < 12; i++) Assert(KeepWorldPointUnderGui(driver, anchor, target), "free anchor correction failed");
                double error = Vector2.Distance(driver.cachedCamera.ProjectGui(anchor), target);
                maxAnchorError = Math.Max(maxAnchorError, error);
                Assert(error < .002, $"free anchor screen error {error:F6}px at {lon}/{lat}");
                Assert(TryGetWorldSpherePointAtGui(target, out Vector3 landed)
                    && (landed - anchor).magnitude / WorldCameraDriver.SphereRadius < 1e-4,
                    "free anchor geographic error");
                Assert(driver.rotationAnimation_lerpFactor == 1, "free anchor left native animation running");
            }
            Console.WriteLine($"PASS free anchor at nonzero world poses; maximum projection error {maxAnchorError:F6} GUI pixels; animations cancelled");
        }
    }
}
