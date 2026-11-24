using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimTouch.TouchPlatform;
using UnityEngine;
using Verse;

namespace RimTouch
{
    public static partial class TouchInputDriver
    {
        private static long proofGesture;
        private static int proofScrollFrame = -1;
        private static int proofScrollEvent = -1;
        private static int proofScrollSlot;
        private static string proofSession;
        private static readonly Dictionary<string, float> ProofScrollSamples = new Dictionary<string, float>();

        private static void EnsureProofSession()
        {
            if (proofSession == TouchInputProof.SessionId) return;
            proofSession = TouchInputProof.SessionId;
            ProofScrollSamples.Clear();
            proofScrollFrame = -1;
            proofScrollEvent = -1;
            proofScrollSlot = 0;
        }

        private struct ScrollProofEvent
        {
            public int Type;
            public bool IsMouse;
            public Vector2 MousePosition;
        }

        private static ScrollProofEvent CaptureScrollProofEvent()
        {
            try
            {
                Event current = Event.current;
                return new ScrollProofEvent
                {
                    Type = current == null ? -1 : (int)current.type,
                    IsMouse = current != null && current.isMouse,
                    MousePosition = current == null ? Vector2.zero : current.mousePosition
                };
            }
            catch (Exception ex)
            {
                TouchInputProof.StopDiagnostics(ex);
                return new ScrollProofEvent { Type = -1 };
            }
        }

        private static object ProofPoint(Vector2 point)
        {
            return new { x = point.x, y = point.y };
        }

        private static object ProofRect(Rect rect)
        {
            return new { x = rect.x, y = rect.y, width = rect.width, height = rect.height };
        }

        private static void RecordTouchLifecycle(string action)
        {
            if (!TouchInputProof.IsRecording) return;
            try
            {
                EnsureProofSession();
                if (action == "begin")
                {
                    proofGesture++;
                    ProofScrollSamples.Clear();
                }
                TouchInputProof.RecordDiagnostic(new
                {
                    kind = "touchLifecycle", session = TouchInputProof.SessionId, utc = DateTime.UtcNow, frame = Time.frameCount,
                    gesture = proofGesture, action, finger = primaryFingerId, mode = mode.ToString(),
                    touchCount = TouchSource.Count, canceled = TouchSource.Canceled,
                    start = ProofPoint(startGui), last = ProofPoint(lastGui), startedOverUi,
                    scrollActive = uiScrollGestureActive, scrollRect = ProofRect(uiScrollActiveRect),
                    inertiaActive = uiScrollInertiaActive, dropoutFrames = 0
                });
            }
            catch (Exception ex)
            {
                TouchInputProof.StopDiagnostics(ex);
            }
        }

        private static void RecordScrollDecision(Rect outRect, Rect viewRect, Vector2 before, Vector2 after,
            bool wasActive, bool wasInertia, Vector2 previousScrollGui,
            ScrollProofEvent inputEvent, bool handled, ScrollDecision scrollDecision)
        {
            if (!TouchInputProof.IsRecording) return;
            try
            {
                EnsureProofSession();
                Event current = Event.current;
                int eventType = inputEvent.Type;
                if (proofScrollFrame != Time.frameCount || proofScrollEvent != eventType)
                {
                    proofScrollFrame = Time.frameCount;
                    proofScrollEvent = eventType;
                    proofScrollSlot = 0;
                }
                int slot = proofScrollSlot++;
                if (TouchSource.Count == 0 && !wasInertia) return;

                bool hasTouch = TouchSource.Count > 0;
                ContactTouch touch = hasTouch ? TouchSource.GetTouch(0) : default(ContactTouch);
                Vector2 gui = hasTouch ? TouchToGui(touch.position) : lastGui;
                // Reconstruct the values used before the core updated uiScrollLastGui.
                Vector2 previous = wasActive ? previousScrollGui
                    : hasTouch ? gui - TouchDeltaToGui(touch.deltaPosition) : lastGui;
                Rect hitRect = outRect;
                Vector2 local = GuiToLocalGuiPoint(gui);
                Vector2 mouse = inputEvent.MousePosition;
                Rect contentRect = hitRect;
                if (viewRect.height - outRect.height > 1f) contentRect.width = Mathf.Max(0f, contentRect.width - 16f);
                if (viewRect.width - outRect.width > 1f) contentRect.height = Mathf.Max(0f, contentRect.height - 16f);
                string hit = contentRect.Contains(ScrollStartLocal()) ? "localStart" : "none";
                Vector2 dragFromStart = gui - startGui;
                bool directionClaim = IsScrollDirectionDrag(Mathf.Max(0f, viewRect.width - outRect.width),
                    Mathf.Max(0f, viewRect.height - outRect.height), dragFromStart);
                Window window = Find.WindowStack == null ? null : Find.WindowStack.currentlyDrawnWindow;
                string decision = ScrollDecisionName(scrollDecision);
                if (decision == "notScrollable" || decision == "otherRect" || decision == "otherWindow") return;
                string key = (window == null ? 0 : RuntimeHelpers.GetHashCode(window)) + ":" + outRect.ToString() + ":" + decision + ":" + hit;
                float now = Time.realtimeSinceStartup;
                float sampledAt;
                // Bound both GUI-pass repetition and bookkeeping. Claims are never sampled away.
                if (decision != "claimed" && ProofScrollSamples.TryGetValue(key, out sampledAt) && now - sampledAt < 0.3f) return;
                if (!ProofScrollSamples.ContainsKey(key) && ProofScrollSamples.Count >= 64 && decision != "claimed") return;
                if (ProofScrollSamples.Count < 64 || ProofScrollSamples.ContainsKey(key)) ProofScrollSamples[key] = now;

                TouchInputProof.RecordDiagnostic(new
                {
                    kind = "scrollDecision", frame = Time.frameCount,
                    gesture = proofGesture, slot, eventType = eventType < 0 ? "none" : ((EventType)eventType).ToString(),
                    eventTypeAfter = current == null ? "none" : current.type.ToString(),
                    dragConsumed = eventType == (int)EventType.MouseDrag && current != null && current.type == EventType.Used,
                    decision, hit, hitTestUsed = decision == "claimed" || decision == "outside" || decision == "belowSlop",
                    directionClaim, dragFromStart = ProofPoint(dragFromStart),
                    handled, finger = hasTouch ? touch.fingerId : -1,
                    phase = hasTouch ? touch.phase.ToString() : "none", touchCount = TouchSource.Count,
                    mode = mode.ToString(), ignoreTouchesUntilAllReleased, startedOverUi,
                    gui = ProofPoint(gui), previous = ProofPoint(previous), local = ProofPoint(local),
                    start = ProofPoint(startGui),
                    localStart = ProofPoint(ScrollStartLocal()), eventMouse = ProofPoint(mouse),
                    eventIsMouse = inputEvent.IsMouse,
                    outRect = ProofRect(outRect), viewRect = ProofRect(viewRect),
                    localInsideViewport = outRect.Contains(local),
                    localStartInsideViewport = contentRect.Contains(ScrollStartLocal()),
                    before = ProofPoint(before), after = ProofPoint(after),
                    wasActive, active = uiScrollGestureActive,
                    wasInertia, inertia = uiScrollInertiaActive,
                    windowType = window == null ? null : window.GetType().FullName,
                    windowId = window == null ? 0 : RuntimeHelpers.GetHashCode(window),
                    windowRect = window == null ? null : ProofRect(window.windowRect)
                });
            }
            catch (Exception ex)
            {
                TouchInputProof.StopDiagnostics(ex);
            }
        }

        private static string ScrollDecisionName(ScrollDecision decision)
        {
            switch (decision)
            {
                case ScrollDecision.Disabled: return "disabled";
                case ScrollDecision.NotScrollable: return "notScrollable";
                case ScrollDecision.Inertia: return "inertia";
                case ScrollDecision.BlockedContacts: return "blockedContacts";
                case ScrollDecision.Ended: return "ended";
                case ScrollDecision.OtherFinger: return "otherFinger";
                case ScrollDecision.Active: return "active";
                case ScrollDecision.OtherRect: return "otherRect";
                case ScrollDecision.OtherWindow: return "otherWindow";
                case ScrollDecision.Claimed: return "claimed";
                case ScrollDecision.Outside: return "outside";
                default: return "belowSlop";
            }
        }
    }
}
