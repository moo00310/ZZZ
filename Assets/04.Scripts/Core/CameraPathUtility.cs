using System.Collections.Generic;
using UnityEngine;

namespace ZZZ
{
    public static class CameraPathUtility
    {
        private const float MIN_KNOT_DISTANCE = 0.0001f;

        public static Vector3 Evaluate(
            IReadOnlyList<Vector3> points, float normalizedTime)
        {
            if (points == null || points.Count == 0) return Vector3.zero;
            if (points.Count == 1) return points[0];

            int segmentCount = points.Count - 1;
            float scaledTime = Mathf.Clamp01(normalizedTime) * segmentCount;
            int segment = Mathf.Min(
                Mathf.FloorToInt(scaledTime), segmentCount - 1);
            float segmentTime = scaledTime - segment;

            Vector3 p1 = points[segment];
            Vector3 p2 = points[segment + 1];
            Vector3 p0 = segment > 0
                ? points[segment - 1]
                : p1 + (p1 - p2);
            Vector3 p3 = segment + 2 < points.Count
                ? points[segment + 2]
                : p2 + (p2 - p1);

            return EvaluateSegment(p0, p1, p2, p3, segmentTime);
        }

        public static float RemapPointTime(IReadOnlyList<float> pointTimes,
            int pointCount, float normalizedTime)
        {
            float time = Mathf.Clamp01(normalizedTime);
            if (pointCount <= 1) return 0f;
            if (pointTimes == null || pointTimes.Count != pointCount)
                return time;

            for (int i = 0; i < pointCount - 1; i++)
            {
                float start = Mathf.Clamp01(pointTimes[i]);
                float end = Mathf.Clamp01(pointTimes[i + 1]);
                if (time > end && i < pointCount - 2) continue;

                float segmentTime = end > start
                    ? Mathf.Clamp01((time - start) / (end - start))
                    : 0f;
                return (i + segmentTime) / (pointCount - 1);
            }
            return 1f;
        }

        public static float EvaluateLinear(
            IReadOnlyList<float> values, float normalizedTime, float fallback)
        {
            if (values == null || values.Count == 0) return fallback;
            if (values.Count == 1) return values[0];

            float scaledTime = Mathf.Clamp01(normalizedTime)
                * (values.Count - 1);
            int segment = Mathf.Min(
                Mathf.FloorToInt(scaledTime), values.Count - 2);
            return Mathf.Lerp(
                values[segment], values[segment + 1], scaledTime - segment);
        }

        private static Vector3 EvaluateSegment(
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float time)
        {
            float t0 = 0f;
            float t1 = t0 + KnotInterval(p0, p1);
            float t2 = t1 + KnotInterval(p1, p2);
            float t3 = t2 + KnotInterval(p2, p3);
            float t = Mathf.Lerp(t1, t2, Mathf.Clamp01(time));

            Vector3 a1 = Interpolate(p0, p1, t0, t1, t);
            Vector3 a2 = Interpolate(p1, p2, t1, t2, t);
            Vector3 a3 = Interpolate(p2, p3, t2, t3, t);
            Vector3 b1 = Interpolate(a1, a2, t0, t2, t);
            Vector3 b2 = Interpolate(a2, a3, t1, t3, t);
            return Interpolate(b1, b2, t1, t2, t);
        }

        private static float KnotInterval(Vector3 a, Vector3 b)
        {
            float distance = Vector3.Distance(a, b);
            return Mathf.Max(MIN_KNOT_DISTANCE, Mathf.Sqrt(distance));
        }

        private static Vector3 Interpolate(
            Vector3 a, Vector3 b, float start, float end, float time)
        {
            float duration = end - start;
            if (duration <= Mathf.Epsilon) return a;

            float weight = (time - start) / duration;
            return Vector3.LerpUnclamped(a, b, weight);
        }
    }
}
