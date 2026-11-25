using RimTouch.TouchPlatform;
using UnityEngine;
using Verse;

namespace RimTouch
{
    public struct TouchTapGuiState
    {
        internal long Sequence;
        internal Event Input;
        internal EventType Type;
        internal Vector2 Position;
        internal bool Matches;
    }

    public static partial class TouchInputDriver
    {
        private struct MapTap
        {
            public long Sequence, Finger;
            public bool Active, Released, MouseDown, MouseUp, IsWorld;
            public Vector2 Start, Release, MouseUpPosition;
            public object Root, World;
            public Map Map;
            public int Width, Height, UiWidth, UiHeight;
        }

        private static MapTap mapTap;
        private static long mapTapSequence;
        private static long mapTapGuiSequence;
        private static EventType mapTapGuiType;

        private static void BeginMapTap()
        {
            CancelMapTap("newGesture");
            if (gestureOwner != TouchOwner.LocalMap && gestureOwner != TouchOwner.WorldMap) return;
            mapTap = new MapTap
            {
                Sequence = ++mapTapSequence, Finger = primaryFingerId, Active = true,
                Start = startGui, IsWorld = gestureIsWorld, Map = gestureMap,
                Root = Find.UIRoot, World = Find.World,
                Width = Screen.width, Height = Screen.height,
                UiWidth = UI.screenWidth, UiHeight = UI.screenHeight
            };
        }

        private static bool MapTapContextValid()
        {
            return TouchModeEnabled && !TouchSource.Canceled
                && ReferenceEquals(mapTap.Root, Find.UIRoot)
                && ReferenceEquals(mapTap.World, Find.World)
                && ReferenceEquals(mapTap.Map, Find.CurrentMap)
                && mapTap.IsWorld == IsWorldMapActive()
                && mapTap.Width == Screen.width && mapTap.Height == Screen.height
                && mapTap.UiWidth == UI.screenWidth && mapTap.UiHeight == UI.screenHeight
                && !(mapTap.IsWorld ? IsWorldMapToolActive() : IsVanillaMapToolActive());
        }

        private static void ValidateMapTap()
        {
            if (mapTap.Active && !MapTapContextValid()) CancelMapTap("contextChanged");
        }

        private static void CancelMapTap(string reason)
        {
            if (mapTap.Active) RecordMapTapDecision(reason);
            mapTap = default(MapTap);
            mapTapGuiSequence = 0;
        }

        private static bool MapTapPointMatches(Vector2 gui)
        {
            return Vector2.Distance(gui, mapTap.Start) <= DragSlopPixels
                && (!mapTap.Released || Vector2.Distance(gui, mapTap.Release) <= DragSlopPixels);
        }

        public static void BeginMapTapGui(out TouchTapGuiState state)
        {
            state = default(TouchTapGuiState);
            mapTapGuiSequence = 0;
            ValidateMapTap();
            if (!mapTap.Active) return;
            Event current = Event.current;
            if (current == null) return;
            state.Sequence = mapTap.Sequence;
            state.Input = current;
            state.Type = current.type;
            state.Position = current.mousePosition;
            mapTapGuiSequence = mapTap.Sequence;
            mapTapGuiType = current.type;
            if (current.type != EventType.MouseDown && current.type != EventType.MouseUp) return;
            if (current.button != 0 || !MapTapPointMatches(state.Position))
            {
                mapTapGuiSequence = 0;
                if (current.type == EventType.MouseDown && mapTap.Released && TouchSource.Count == 0)
                    CancelMapTap("newMouse");
                return;
            }
            if (current.type == EventType.MouseDown)
            {
                // Only a live sample may bind a mouse-down to this physical touch.
                if (mapTap.MouseDown || TouchSource.Count != 1
                    || TouchSource.GetTouch(0).fingerId != mapTap.Finger)
                {
                    CancelMapTap("unpairedMouseDown");
                    return;
                }
                mapTap.MouseDown = true;
                RecordMapTapDecision("mouseDown");
            }
            state.Matches = mapTap.MouseDown;
            if (!state.Matches)
            {
                mapTapGuiSequence = 0;
                RecordMapTapDecision("unpairedMouseUp");
            }
        }

        public static void EndMapTapGui(TouchTapGuiState state)
        {
            mapTapGuiSequence = 0;
            ValidateMapTap();
            if (!mapTap.Active || state.Sequence != mapTap.Sequence || !state.Matches) return;
            if (!ReferenceEquals(state.Input, Event.current))
            {
                CancelMapTap("eventReplaced");
                return;
            }
            if (state.Input.type == EventType.Used)
            {
                CancelMapTap("uiConsumed");
                return;
            }
            if (state.Type == EventType.MouseUp)
            {
                mapTap.MouseUp = true;
                mapTap.MouseUpPosition = state.Position;
                RecordMapTapDecision("mouseUp");
                TryCommitMapTap();
            }
        }

        public static void CancelMapTapGui()
        {
            CancelMapTap("guiFailed");
        }

        public static void NotifyUiTapHandled(Rect rect)
        {
            if (!mapTap.Active || mapTapGuiSequence != mapTap.Sequence) return;
            Event current = Event.current;
            if (mapTapGuiType != EventType.MouseDown && mapTapGuiType != EventType.MouseUp
                && !((mapTapGuiType == EventType.Layout || mapTapGuiType == EventType.Repaint)
                    && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0)))) return;
            bool contactMatches = TouchSource.Count == 1
                && TouchSource.GetTouch(0).fingerId == mapTap.Finger;
            if (current == null || (!contactMatches && !(mapTap.MouseDown && Input.GetMouseButtonUp(0)))) return;
            // The control's current local rectangle is authoritative; no layout cache.
            Vector2 gui = mapTap.Released ? mapTap.Release : mapTap.Start;
            if (rect.Contains(GuiToLocalGuiPoint(gui))) CancelMapTap("uiControl");
        }

        private static void ReleaseMapTap(Vector2 gui)
        {
            ValidateMapTap();
            if (!mapTap.Active || primaryFingerId != mapTap.Finger) return;
            mapTap.Release = gui;
            mapTap.Released = true;
            RecordMapTapDecision("released");
            TryCommitMapTap();
        }

        private static void TryCommitMapTap()
        {
            if (!mapTap.Active || !mapTap.Released || !mapTap.MouseUp) return;
            if (!MapTapContextValid() || !MapTapPointMatches(mapTap.MouseUpPosition)
                || IsBlockingWindowAt(mapTap.Release))
            {
                CancelMapTap("blockedAtCommit");
                return;
            }
            Vector2 gui = mapTap.Release;
            bool world = mapTap.IsWorld;
            RecordMapTapDecision("committed");
            mapTap = default(MapTap);
            if (world)
            {
                lastWorldTapTime = Time.realtimeSinceStartup;
                lastWorldTapGui = gui;
                DoWorldLeftClick(gui);
            }
            else if (!TryDoDraftedPawnTapCommand(gui))
            {
                DoMapLeftClick(gui);
            }
        }
    }
}
