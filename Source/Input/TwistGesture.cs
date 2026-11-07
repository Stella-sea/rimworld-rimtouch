// Independent two-contact twist classification. GPL-3.0-or-later.
using System;

namespace RimTouch.TouchPlatform
{
    public sealed class TwistGesture
    {
        // Twist commits once the fingers have rotated this far in total.
        private const float TwistCommitDegrees = 4f;
        // A pinch is recognised when the finger separation changes by this
        // much while the accumulated twist is still below the commit
        // threshold. A real pinch moves the fingers apart/together fast,
        // while the angle between them only wobbles a few degrees; without
        // this check that wobble alone was enough to commit a twist, so
        // every pinch zoom also rolled the globe.
        private const float PinchDominancePixels = 40f;
        // After a twist has committed, stop applying it if the gesture
        // turns into a pinch instead (separation keeps changing while the
        // rotation since the commit stays small).
        private const float PinchTakeoverPixels = 60f;
        private const float PinchTakeoverMaxTwistDegrees = 8f;
        private bool tracking, committed;
        private float previousAngle, pending;
        private float startDistance;
        private float commitDistance, twistSinceCommit;
        private bool pinchLocked;
        public bool HasTwisted { get; private set; }
        public void Reset()
        {
            tracking = committed = HasTwisted = false;
            previousAngle = pending = 0;
            startDistance = commitDistance = twistSinceCommit = 0;
            pinchLocked = false;
        }
        public float Update(float ax, float ay, float bx, float by)
        {
            float dx = bx-ax, dy = by-ay;
            if (float.IsNaN(dx) || float.IsInfinity(dx) || float.IsNaN(dy) || float.IsInfinity(dy) || dx*dx+dy*dy < 32f*32f)
            { tracking = committed = false; pending = 0; return 0; }
            float angle = (float)(Math.Atan2(dy,dx)*180/Math.PI);
            float distance = (float)Math.Sqrt(dx*dx+dy*dy);
            if (!tracking) { tracking = true; previousAngle = angle; startDistance = distance; return 0; }
            float delta = angle-previousAngle;
            previousAngle = angle;
            if (delta > 180) delta -= 360;
            if (delta < -180) delta += 360;
            // A contact crossing or sample discontinuity should not flip the globe.
            if (Math.Abs(delta) > 90) { committed = false; pending = 0; return 0; }
            if (pinchLocked) return 0;
            if (committed)
            {
                twistSinceCommit += delta;
                // The gesture turned into a pinch after all: separation is
                // changing a lot while the rotation stays small. Stop
                // rotating and do not re-commit for this gesture.
                if (Math.Abs(distance - commitDistance) > PinchTakeoverPixels
                    && Math.Abs(twistSinceCommit) < PinchTakeoverMaxTwistDegrees)
                {
                    committed = false;
                    pinchLocked = true;
                    twistSinceCommit = 0;
                    return 0;
                }
                return delta;
            }
            // Not committed yet: a large separation change with only a small
            // accumulated twist means this is a pinch, not a twist.
            if (Math.Abs(distance - startDistance) > PinchDominancePixels && Math.Abs(pending) < TwistCommitDegrees)
            {
                pinchLocked = true;
                pending = 0;
                return 0;
            }
            pending += delta;
            if (Math.Abs(pending) < TwistCommitDegrees) return 0;
            committed = HasTwisted = true;
            commitDistance = distance;
            twistSinceCommit = pending;
            float result = pending;
            pending = 0;
            return result;
        }
    }
}
