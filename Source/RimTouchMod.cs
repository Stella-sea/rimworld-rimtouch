using UnityEngine;
using Verse;
using System.Runtime.CompilerServices;
using RimTouch.TouchPlatform;

namespace RimTouch
{
    public sealed class RimTouchMod : Mod
    {
        internal const bool DefaultExtendedZoomEnabled = true;
        internal const float DefaultExtendedZoomInMin = 5f;
        internal const float DefaultExtendedZoomOutMax = 120f;

        public static RimTouchSettings Settings;
        private static readonly ConditionalWeakTable<CameraMapConfig, ZoomRangeLease> ZoomLeases = new ConditionalWeakTable<CameraMapConfig, ZoomRangeLease>();
        private Vector2 settingsScroll;
        private float settingsHeight = 1000f;
        public static bool ExtraUiScalesEnabled { get { return RimTouchBootstrap.UiScaleHookReady && Settings != null && Settings.enableTouchMode && Settings.allowExtraUiScales; } }

        public RimTouchMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimTouchSettings>();
        }

        public override string SettingsCategory()
        {
            return "RimTouch";
        }

        // Section header, RimTalk-style: a medium-font label with a rule under it,
        // so the long settings list reads as grouped sections instead of one flat wall.
        private static void SectionHeader(Listing_Standard listing, string key)
        {
            Text.Font = GameFont.Medium;
            listing.Label(key.Translate());
            Text.Font = GameFont.Small;
            listing.GapLine();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, Mathf.Max(inRect.height, settingsHeight));
            Widgets.BeginScrollView(inRect, ref settingsScroll, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled("RimTouch.EnableTouchMode".Translate(), ref Settings.enableTouchMode, "RimTouch.EnableTouchModeDesc".Translate());
            SectionHeader(listing, "RimTouch.SectionTouch");
            listing.CheckboxLabeled("RimTouch.EnableTouchMode".Translate(), ref Settings.enableTouchMode, "RimTouch.EnableTouchModeDesc".Translate());
            listing.Label("RimTouch.Input".Translate() + TouchSource.Status);
            listing.CheckboxLabeled("RimTouch.AllowExtraUiScales".Translate(), ref Settings.allowExtraUiScales, "RimTouch.AllowExtraUiScalesDesc".Translate());
            if (!RimTouchBootstrap.UiScaleHookReady) listing.Label("RimTouch.ExtraScalesUnavailable".Translate());
            if (listing.ButtonText("RimTouch.ResetUiScale".Translate()))
            {
                Settings.allowExtraUiScales = false;
                Prefs.UIScale = 1f;
            }
            listing.Gap();

            SectionHeader(listing, "RimTouch.SectionWorldMap");
            listing.CheckboxLabeled("RimTouch.EnableGlobeTwist".Translate(), ref Settings.enableGlobeTwist, "RimTouch.EnableGlobeTwistDesc".Translate());
            listing.Label("RimTouch.TwistCommitDegrees".Translate() + Settings.twistCommitDegrees.ToString("0") + "\u00b0");
            Settings.twistCommitDegrees = listing.Slider(Settings.twistCommitDegrees, 3f, 20f);
            listing.Label("RimTouch.TwistCommitDegreesDesc".Translate());
            listing.CheckboxLabeled("RimTouch.LockNorthUpOnDrag".Translate(), ref Settings.lockNorthUpOnDrag, "RimTouch.LockNorthUpOnDragDesc".Translate());
            listing.Label("RimTouch.TurntableDragSensitivity".Translate() + Settings.turntableDragSensitivity.ToString("0.00") + "x");
            Settings.turntableDragSensitivity = listing.Slider(Settings.turntableDragSensitivity, 0.5f, 2f);
            listing.Label("RimTouch.TurntableDragSensitivityDesc".Translate());
            listing.CheckboxLabeled("RimTouch.InvertTurntableAxis".Translate(), ref Settings.invertTurntableAxis, "RimTouch.InvertTurntableAxisDesc".Translate());
            listing.CheckboxLabeled("RimTouch.AutoLockNorthOnDrag".Translate(), ref Settings.autoLockNorthOnDrag, "RimTouch.AutoLockNorthOnDragDesc".Translate());
            listing.Gap();

            SectionHeader(listing, "RimTouch.SectionInertia");

            listing.Label("RimTouch.PanInertiaStrength".Translate() + Settings.panInertiaStrength.ToString("0.00"));
            Settings.panInertiaStrength = listing.Slider(Settings.panInertiaStrength, 0f, 2f);

            listing.Label("RimTouch.PanInertiaDamping".Translate() + Settings.panInertiaDamping.ToString("0.00"));
            Settings.panInertiaDamping = listing.Slider(Settings.panInertiaDamping, 1f, 10f);

            listing.Label("RimTouch.ZoomInertiaStrength".Translate() + Settings.zoomInertiaStrength.ToString("0.00"));
            Settings.zoomInertiaStrength = listing.Slider(Settings.zoomInertiaStrength, 0f, 2f);

            listing.Label("RimTouch.ZoomInertiaDamping".Translate() + Settings.zoomInertiaDamping.ToString("0.00"));
            Settings.zoomInertiaDamping = listing.Slider(Settings.zoomInertiaDamping, 1f, 10f);

            listing.Gap();
            if (listing.ButtonText("RimTouch.ResetTouchTuning".Translate()))
            {
                Settings.ResetTouchTuning();
            }

            listing.Gap();
            SectionHeader(listing, "RimTouch.SectionZoomRange");
            listing.Label("RimTouch.ExtendedZoomRangeNote".Translate());
            bool extendedZoomEnabledBefore = Settings.extendedZoomEnabled;
            listing.CheckboxLabeled("RimTouch.EnableExtendedZoomRange".Translate(), ref Settings.extendedZoomEnabled, "RimTouch.EnableExtendedZoomRangeDesc".Translate());

            listing.Label("RimTouch.ZoomInMin".Translate() + Settings.extendedZoomInMin.ToString("0.0"));
            Settings.extendedZoomInMin = listing.Slider(Settings.extendedZoomInMin, 0.5f, 11f);

            listing.Label("RimTouch.ZoomOutMax".Translate() + Settings.extendedZoomOutMax.ToString("0"));
            Settings.extendedZoomOutMax = listing.Slider(Settings.extendedZoomOutMax, 60f, 300f);

            listing.Gap();
            if (listing.ButtonText("RimTouch.ApplyZoomRangeNow".Translate()))
            {
                ApplyExtendedZoomRange();
            }
            if (listing.ButtonText("RimTouch.ResetZoomRange".Translate()))
            {
                Settings.ResetZoomRange();
                ApplyExtendedZoomRange();
            }

            if (Settings.extendedZoomEnabled != extendedZoomEnabledBefore || Settings.extendedZoomEnabled)
            {
                // Re-apply live so dragging the sliders gives immediate feedback in-game instead
                // of only taking effect after the next save/load.
                ApplyExtendedZoomRange();
            }

            settingsHeight = listing.CurHeight;
            listing.End();
            Widgets.EndScrollView();
            // Dialog_ModSettings.PreClose persists through Mod.WriteSettings.
            // Writing on every GUI pass causes needless I/O and file-sharing errors.
        }

        public static void ApplyExtendedZoomRange()
        {
            if (Settings == null)
            {
                return;
            }

            CameraDriver cameraDriver = Find.CameraDriver;
            if (cameraDriver == null || cameraDriver.config == null)
            {
                return;
            }

            float min = Mathf.Clamp(Settings.extendedZoomInMin, 0.5f, 11f);
            float max = Mathf.Max(min + 1f, Settings.extendedZoomOutMax);
            float nextMin, nextMax;
            ZoomRangeLease lease = ZoomLeases.GetOrCreateValue(cameraDriver.config);
            if (lease.Update(cameraDriver.config.sizeRange.min, cameraDriver.config.sizeRange.max, Settings.extendedZoomEnabled, min, max, out nextMin, out nextMax))
            {
                cameraDriver.config.sizeRange.min = nextMin;
                cameraDriver.config.sizeRange.max = nextMax;
            }
        }
    }

    public sealed class RimTouchSettings : ModSettings
    {
        public bool enableTouchMode = true;
        public bool enableGlobeTwist = true;
        public float twistCommitDegrees = 10f;
        public bool lockNorthUpOnDrag = true;
        public float turntableDragSensitivity = 1f;
        public bool invertTurntableAxis = false;
        public bool autoLockNorthOnDrag = false;
        public bool allowExtraUiScales;
        public float panInertiaStrength = 1.05f;
        public float panInertiaDamping = 4.8f;
        public float zoomInertiaStrength = 1.05f;
        public float zoomInertiaDamping = 4.8f;
        public bool extendedZoomEnabled = RimTouchMod.DefaultExtendedZoomEnabled;
        public float extendedZoomInMin = RimTouchMod.DefaultExtendedZoomInMin;
        public float extendedZoomOutMax = RimTouchMod.DefaultExtendedZoomOutMax;
        public int settingsVersion = 6;

        public void ResetTouchTuning()
        {
            panInertiaStrength = 1.05f;
            panInertiaDamping = 4.8f;
            zoomInertiaStrength = 1.05f;
            zoomInertiaDamping = 4.8f;
        }

        public void ResetZoomRange()
        {
            extendedZoomEnabled = RimTouchMod.DefaultExtendedZoomEnabled;
            extendedZoomInMin = RimTouchMod.DefaultExtendedZoomInMin;
            extendedZoomOutMax = RimTouchMod.DefaultExtendedZoomOutMax;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableTouchMode, "enableTouchMode", true);
            Scribe_Values.Look(ref enableGlobeTwist, "enableGlobeTwist", true);
            Scribe_Values.Look(ref twistCommitDegrees, "twistCommitDegrees", 10f);
            Scribe_Values.Look(ref lockNorthUpOnDrag, "lockNorthUpOnDrag", true);
            Scribe_Values.Look(ref turntableDragSensitivity, "turntableDragSensitivity", 1f);
            Scribe_Values.Look(ref invertTurntableAxis, "invertTurntableAxis", false);
            Scribe_Values.Look(ref autoLockNorthOnDrag, "autoLockNorthOnDrag", false);
            Scribe_Values.Look(ref allowExtraUiScales, "allowExtraUiScales", false);
            Scribe_Values.Look(ref panInertiaStrength, "panInertiaStrength", 1.05f);
            Scribe_Values.Look(ref panInertiaDamping, "panInertiaDamping", 4.8f);
            Scribe_Values.Look(ref zoomInertiaStrength, "zoomInertiaStrength", 1.05f);
            Scribe_Values.Look(ref zoomInertiaDamping, "zoomInertiaDamping", 4.8f);
            Scribe_Values.Look(ref extendedZoomEnabled, "extendedZoomEnabled", RimTouchMod.DefaultExtendedZoomEnabled);
            Scribe_Values.Look(ref extendedZoomInMin, "extendedZoomInMin", RimTouchMod.DefaultExtendedZoomInMin);
            Scribe_Values.Look(ref extendedZoomOutMax, "extendedZoomOutMax", RimTouchMod.DefaultExtendedZoomOutMax);
            Scribe_Values.Look(ref settingsVersion, "settingsVersion", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int loadedVersion = settingsVersion;
                if (loadedVersion < 1)
                {
                    if (Mathf.Abs(zoomInertiaStrength - 0.22f) < 0.001f)
                    {
                        zoomInertiaStrength = 1.05f;
                    }
                    if (Mathf.Abs(zoomInertiaDamping - 7.5f) < 0.001f)
                    {
                        zoomInertiaDamping = 4.8f;
                    }
                }
                if (loadedVersion < 2)
                {
                    extendedZoomEnabled = RimTouchMod.DefaultExtendedZoomEnabled;
                    if (Mathf.Abs(extendedZoomOutMax - 90f) < 0.001f)
                    {
                        extendedZoomOutMax = RimTouchMod.DefaultExtendedZoomOutMax;
                    }
                }
                settingsVersion = 6;
            }
        }
    }
}
