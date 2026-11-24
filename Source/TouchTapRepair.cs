using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimTouch
{
    public static class TouchTapRepair
    {
        private const float DoubleTapMaxSeconds = 0.38f;
        private const float DoubleTapMaxDistancePixels = 36f;
        private const float TouchButtonPaddingPixels = 8f;
        private const float MaxExpandedButtonSidePixels = 120f;

        private static bool pendingTap;
        private static Vector2 tapGuiPosition;
        private static int tapFrame;
        private static int pendingClickCount = 1;
        private static bool hasLastTap;
        private static Vector2 lastTapGuiPosition;
        private static float lastTapTime;
        private static int suppressWindowCloseUntilFrame;
        private static int draggableTriggerFrame = -1;
        private static readonly List<DraggableTrigger> draggableTriggers = new List<DraggableTrigger>(8);

        private struct DraggableTrigger
        {
            public Window window;
            public Rect rect;
        }

        public static bool ShouldSuppressDuplicateDraggableInFrame(Rect rect)
        {
            ResetDraggableTriggersForFrame();
            Window window = Find.WindowStack == null ? null : Find.WindowStack.currentlyDrawnWindow;
            Rect screenRect = DraggableScreenRect(rect);
            for (int i = 0; i < draggableTriggers.Count; i++)
            {
                DraggableTrigger trigger = draggableTriggers[i];
                if (ReferenceEquals(trigger.window, window)
                    && Mathf.Abs(trigger.rect.x - screenRect.x) <= 0.5f
                    && Mathf.Abs(trigger.rect.y - screenRect.y) <= 0.5f
                    && Mathf.Abs(trigger.rect.width - screenRect.width) <= 0.5f
                    && Mathf.Abs(trigger.rect.height - screenRect.height) <= 0.5f)
                {
                    return true;
                }
            }
            return false;
        }

        public static void NotifyDraggableTriggered(Rect rect)
        {
            ResetDraggableTriggersForFrame();
            draggableTriggers.Add(new DraggableTrigger
            {
                window = Find.WindowStack == null ? null : Find.WindowStack.currentlyDrawnWindow,
                rect = DraggableScreenRect(rect)
            });
            pendingTap = false;
        }

        private static void ResetDraggableTriggersForFrame()
        {
            if (draggableTriggerFrame != Time.frameCount)
            {
                draggableTriggers.Clear();
                draggableTriggerFrame = Time.frameCount;
            }
        }

        private static Rect DraggableScreenRect(Rect rect)
        {
            try
            {
                Vector2 position = GUIUtility.GUIToScreenPoint(new Vector2(rect.x, rect.y));
                return new Rect(position.x, position.y, rect.width, rect.height);
            }
            catch
            {
                return rect;
            }
        }

        public static bool ShouldSuppressWindowClose
        {
            get
            {
                return Time.frameCount <= suppressWindowCloseUntilFrame;
            }
        }

        public static int QueueTap(Vector2 guiPosition)
        {
            pendingTap = true;
            tapGuiPosition = guiPosition;
            tapFrame = Time.frameCount;
            pendingClickCount = RegisterTap(guiPosition);
            return pendingClickCount;
        }

        public static int RegisterTap(Vector2 guiPosition)
        {
            float now = Time.realtimeSinceStartup;
            int clickCount = 1;
            if (hasLastTap
                && now - lastTapTime <= DoubleTapMaxSeconds
                && Vector2.Distance(guiPosition, lastTapGuiPosition) <= DoubleTapMaxDistancePixels)
            {
                clickCount = 2;
                hasLastTap = false;
            }
            else
            {
                hasLastTap = true;
                lastTapGuiPosition = guiPosition;
                lastTapTime = now;
            }

            return clickCount;
        }

        public static bool TryConsumeTap(Rect rect)
        {
            int clickCount;
            return TryConsumeTap(rect, 1, out clickCount);
        }

        public static bool TryConsumeDoubleTap(Rect rect)
        {
            int clickCount;
            return TryConsumeTap(rect, 2, out clickCount);
        }

        private static bool TryConsumeTap(Rect rect, int minClickCount, out int clickCount)
        {
            clickCount = 0;
            if (!pendingTap)
            {
                return false;
            }

            Event current = Event.current;
            if (current == null || current.type != EventType.MouseUp || current.button != 0)
            {
                return false;
            }

            if (Time.frameCount - tapFrame > 8)
            {
                pendingTap = false;
                return false;
            }

            if (pendingClickCount < minClickCount)
            {
                return false;
            }

            Rect hitRect = GetTouchHitRect(rect);
            Vector2 consumePosition;
            try
            {
                consumePosition = TouchInputDriver.GuiToLocalGuiPoint(tapGuiPosition);
            }
            catch
            {
                return false;
            }
            if (!hitRect.Contains(consumePosition))
            {
                return false;
            }

            pendingTap = false;
            suppressWindowCloseUntilFrame = Time.frameCount + 2;
            clickCount = pendingClickCount;
            current.clickCount = pendingClickCount;
            current.mousePosition = consumePosition;
            current.Use();
            return true;
        }

        public static void Clear(bool clearTapHistory = true)
        {
            // Preserve this frame's draggable triggers even if a control disables touch.
            pendingTap = false;
            pendingClickCount = 1;

            if (clearTapHistory)
            {
                hasLastTap = false;
                lastTapGuiPosition = Vector2.zero;
                lastTapTime = 0f;
            }
        }

        private static Rect GetTouchHitRect(Rect rect)
        {
            if (rect.width > MaxExpandedButtonSidePixels || rect.height > MaxExpandedButtonSidePixels)
            {
                return rect;
            }

            return new Rect(
                rect.xMin - TouchButtonPaddingPixels,
                rect.yMin - TouchButtonPaddingPixels,
                rect.width + TouchButtonPaddingPixels * 2f,
                rect.height + TouchButtonPaddingPixels * 2f);
        }

    }
}
