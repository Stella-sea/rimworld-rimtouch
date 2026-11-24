using System;
using UnityEngine;

namespace RimTouch.TouchPlatform
{
    public sealed class VelocityHistory
    {
        private readonly Vector3[] velocities;
        private readonly float[] times;
        private readonly float historySeconds;
        private int nextIndex;
        private int count;

        public VelocityHistory(int capacity, float historySeconds)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException("capacity");
            }
            if (historySeconds <= 0f || float.IsNaN(historySeconds) || float.IsInfinity(historySeconds))
            {
                throw new ArgumentOutOfRangeException("historySeconds");
            }
            velocities = new Vector3[capacity];
            times = new float[capacity];
            this.historySeconds = historySeconds;
        }

        public bool HasSamples
        {
            get { return count > 0; }
        }

        public void Clear()
        {
            nextIndex = 0;
            count = 0;
        }

        public void Record(Vector3 velocity, float time)
        {
            velocities[nextIndex] = velocity;
            times[nextIndex] = time;
            nextIndex = (nextIndex + 1) % velocities.Length;
            count = Mathf.Min(count + 1, velocities.Length);
        }

        public Vector3 GetWeightedAverage(float now)
        {
            if (count <= 0)
            {
                return Vector3.zero;
            }
            Vector3 sum = Vector3.zero;
            float totalWeight = 0f;
            for (int i = 0; i < count; i++)
            {
                int index = (nextIndex - 1 - i + velocities.Length) % velocities.Length;
                float age = now - times[index];
                if (age < 0f || age > historySeconds)
                {
                    continue;
                }

                float recencyWeight = Mathf.Clamp01(1f - age / historySeconds);
                float orderWeight = 1f / (1f + i * 0.35f);
                float weight = Mathf.Max(0.05f, recencyWeight) * orderWeight;
                sum += velocities[index] * weight;
                totalWeight += weight;
            }
            if (totalWeight <= 0f)
            {
                return Vector3.zero;
            }
            return sum / totalWeight;
        }

        public void Record2D(Vector2 velocity, float time)
        {
            Record(new Vector3(velocity.x, velocity.y, 0f), time);
        }

        public Vector2 GetWeightedAverage2D(float now)
        {
            Vector3 velocity = GetWeightedAverage(now);
            return new Vector2(velocity.x, velocity.y);
        }

        public void Record1D(float velocity, float time)
        {
            Record(new Vector3(velocity, 0f, 0f), time);
        }

        public float GetWeightedAverage1D(float now)
        {
            return GetWeightedAverage(now).x;
        }
    }
}
