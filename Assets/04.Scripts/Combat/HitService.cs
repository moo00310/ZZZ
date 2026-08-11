using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Combat
{
    public static class HitService
    {
        private static readonly Collider[] s_hits = new Collider[64];
        private static readonly RaycastHit[] s_sweepHits = new RaycastHit[64];

        public static void Execute(HitData definition, HitExecutionContext context)
        {
            HitHandle handle = Begin(definition, context);
            handle?.Stop();
        }

        public static HitHandle Begin(HitData definition, HitExecutionContext context)
        {
            if (definition == null || context.Source == null) return null;

            var handle = new HitHandle(definition, context);
            handle.Tick(0f, 0f);
            return handle;
        }

        internal static bool Query(HitHandle handle, float normalizedProgress)
        {
            HitData definition = handle.Definition;
            Transform origin = handle.ResolveOrigin();
            if (definition == null || origin == null) return false;

            Quaternion rotation = origin.rotation * definition.RotationOffset;
            Vector3 center = origin.TransformPoint(definition.PositionOffset);
            float currentRadius = definition.Shape == HitShape.ExpandingSphere
                ? Mathf.Max(0f, definition.EvaluateRadius(normalizedProgress))
                : definition.Radius;

            int count = QueryOverlap(definition, center, rotation, currentRadius);
            for (int i = 0; i < count; i++)
                ProcessCollider(handle, s_hits[i], center, rotation, false);

            bool sweep = definition.QueryMode == HitQueryMode.Sweep
                && handle.HasPreviousPose;
            if (sweep)
            {
                int sweepCount = QuerySweep(
                    definition, handle, center, currentRadius);
                for (int i = 0; i < sweepCount; i++)
                    ProcessCollider(
                        handle, s_sweepHits[i].collider, center, rotation, true);
            }

            if (handle.DebugDraw)
                HitDebugDraw.Draw(
                    definition, center, rotation, currentRadius,
                    handle.HasPreviousPose, handle.PreviousCenter,
                    handle.PreviousRotation, handle.PreviousRadius,
                    handle.DebugDuration);

            handle.SetPreviousPose(center, rotation, currentRadius);
            return true;
        }

        private static int QueryOverlap(
            HitData definition, Vector3 center, Quaternion rotation,
            float currentRadius)
        {
            switch (definition.Shape)
            {
                case HitShape.Box:
                    return Physics.OverlapBoxNonAlloc(
                        center, definition.BoxSize * 0.5f, s_hits, rotation,
                        definition.TargetMask, definition.TriggerInteraction);
                case HitShape.Capsule:
                    Vector3 start = center;
                    Vector3 end = center + rotation * Vector3.forward * definition.Length;
                    return Physics.OverlapCapsuleNonAlloc(
                        start, end, definition.Radius, s_hits,
                        definition.TargetMask, definition.TriggerInteraction);
                default:
                    return Physics.OverlapSphereNonAlloc(
                        center, currentRadius, s_hits,
                        definition.TargetMask, definition.TriggerInteraction);
            }
        }

        private static int QuerySweep(
            HitData definition, HitHandle handle, Vector3 center,
            float currentRadius)
        {
            Vector3 delta = center - handle.PreviousCenter;
            float distance = delta.magnitude;
            if (distance <= 0.0001f) return 0;
            Vector3 direction = delta / distance;

            switch (definition.Shape)
            {
                case HitShape.Box:
                    return Physics.BoxCastNonAlloc(
                        handle.PreviousCenter, definition.BoxSize * 0.5f,
                        direction, s_sweepHits, handle.PreviousRotation,
                        distance, definition.TargetMask,
                        definition.TriggerInteraction);
                case HitShape.Capsule:
                    Vector3 previousStart = handle.PreviousCenter;
                    Vector3 previousEnd = previousStart
                        + handle.PreviousRotation * Vector3.forward * definition.Length;
                    return Physics.CapsuleCastNonAlloc(
                        previousStart, previousEnd, definition.Radius,
                        direction, s_sweepHits, distance,
                        definition.TargetMask, definition.TriggerInteraction);
                default:
                    float sweepRadius = Mathf.Max(
                        handle.PreviousRadius, currentRadius);
                    return Physics.SphereCastNonAlloc(
                        handle.PreviousCenter, sweepRadius, direction,
                        s_sweepHits, distance, definition.TargetMask,
                        definition.TriggerInteraction);
            }
        }

        private static void ProcessCollider(
            HitHandle handle, Collider hitCollider, Vector3 center,
            Quaternion rotation, bool swept)
        {
            if (hitCollider == null) return;

            IHittable target = hitCollider.GetComponentInParent<IHittable>();
            if (!CanHit(handle, target)) return;

            HitData definition = handle.Definition;
            Vector3 hitPoint = hitCollider.ClosestPoint(center);
            Vector3 toTarget = hitPoint - center;
            if (definition.Shape == HitShape.Cone)
            {
                bool insideCurrent = IsInsideCone(
                    rotation * Vector3.forward, toTarget, definition.Angle);
                bool insidePrevious = swept && IsInsideCone(
                    handle.PreviousRotation * Vector3.forward,
                    hitPoint - handle.PreviousCenter, definition.Angle);
                if (!insideCurrent && !insidePrevious) return;
            }
            if (definition.Shape == HitShape.ExpandingSphere
                && definition.Frequency == HitFrequency.OncePerActivation
                && handle.HasPreviousPose
                && toTarget.magnitude + 0.001f < handle.PreviousRadius)
                return;

            Vector3 direction = toTarget.sqrMagnitude > 0.0001f
                ? toTarget.normalized
                : rotation * Vector3.forward;
            var hitContext = new HitContext(
                definition, handle.Source, handle.SourceTeam, hitPoint, direction);
            if (target.ReceiveHit(in hitContext) == HitResult.Accepted)
                handle.MarkStruck(target);
        }

        public static bool IsInsideCone(Vector3 forward, Vector3 toTarget, float angle)
        {
            if (toTarget.sqrMagnitude <= 0.0001f) return true;
            return Vector3.Angle(forward, toTarget) <= angle * 0.5f;
        }

        internal static Transform ResolveOrigin(
            HitData definition, HitExecutionContext context)
        {
            switch (definition.Origin)
            {
                case HitOrigin.Effect:
                    if (context.EffectOrigin != null) return context.EffectOrigin;
                    if (context.OriginResolver != null
                        && context.OriginResolver.TryResolve(
                            definition.EffectKey, out Transform effectOrigin))
                        return effectOrigin;
                    return null;
                case HitOrigin.Socket:
                    Transform socket = FindRecursive(context.Source, definition.Socket);
                    return socket != null ? socket : context.Source;
                default:
                    return context.Source;
            }
        }

        internal static CombatTeam ResolveTeam(Transform source)
        {
            IHitSource hitSource = source.GetComponentInParent<IHitSource>();
            return hitSource != null ? hitSource.Team : CombatTeam.Neutral;
        }

        private static bool CanHit(HitHandle handle, IHittable target)
        {
            if (target == null || target.HitTransform == null) return false;
            if (target.HitTransform == handle.Source
                || target.HitTransform.IsChildOf(handle.Source)) return false;
            if (!handle.Definition.FriendlyFire
                && handle.SourceTeam != CombatTeam.Neutral
                && target.Team == handle.SourceTeam) return false;
            return !handle.HasStruck(target);
        }

        private static Transform FindRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName)) return null;
            if (root.name == targetName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindRecursive(root.GetChild(i), targetName);
                if (found != null) return found;
            }
            return null;
        }
    }

    internal static class HitDebugDraw
    {
        private static readonly Color CurrentColor = new Color(1f, 0.2f, 0.1f, 1f);
        private static readonly Color SweepColor = new Color(1f, 0.75f, 0.1f, 0.8f);
        private static readonly Vector3[] BoxCorners = new Vector3[8];
        private static Vector3 s_lineRight;
        private static Vector3 s_lineUp;
        private const int SEGMENTS = 24;

        internal static void Draw(
            HitData definition, Vector3 center, Quaternion rotation,
            float radius, bool hasPreviousPose, Vector3 previousCenter,
            Quaternion previousRotation, float previousRadius, float duration)
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                float width = Mathf.Clamp(
                    Vector3.Distance(camera.transform.position, center) * 0.002f,
                    0.008f, 0.04f);
                s_lineRight = camera.transform.right * width;
                s_lineUp = camera.transform.up * width;
            }
            else
            {
                s_lineRight = Vector3.right * 0.01f;
                s_lineUp = Vector3.up * 0.01f;
            }

            DrawShape(definition, center, rotation, radius, CurrentColor, duration);
            if (definition.QueryMode != HitQueryMode.Sweep || !hasPreviousPose) return;

            DrawLine(previousCenter, center, SweepColor, duration);
            DrawShape(definition, previousCenter, previousRotation,
                previousRadius, SweepColor, duration);
        }

        private static void DrawShape(
            HitData definition, Vector3 center, Quaternion rotation,
            float radius, Color color, float duration)
        {
            switch (definition.Shape)
            {
                case HitShape.Sphere:
                case HitShape.ExpandingSphere:
                    DrawSphere(center, rotation, radius, color, duration);
                    break;
                case HitShape.Cone:
                    DrawCone(center, rotation, definition.Radius,
                        definition.Angle, color, duration);
                    break;
                case HitShape.Box:
                    DrawBox(center, rotation, definition.BoxSize, color, duration);
                    break;
                case HitShape.Capsule:
                    DrawCapsule(center, rotation, definition.Radius,
                        definition.Length, color, duration);
                    break;
            }
        }

        private static void DrawSphere(
            Vector3 center, Quaternion rotation, float radius,
            Color color, float duration)
        {
            if (radius <= 0f) return;
            DrawCircle(center, rotation * Vector3.right, radius, color, duration);
            DrawCircle(center, rotation * Vector3.up, radius, color, duration);
            DrawCircle(center, rotation * Vector3.forward, radius, color, duration);
        }

        private static void DrawCircle(
            Vector3 center, Vector3 normal, float radius,
            Color color, float duration)
        {
            Vector3 tangent = Vector3.Cross(
                normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f
                    ? Vector3.right
                    : Vector3.up).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
            Vector3 previous = center + tangent * radius;
            for (int i = 1; i <= SEGMENTS; i++)
            {
                float angle = i * Mathf.PI * 2f / SEGMENTS;
                Vector3 current = center
                    + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle))
                    * radius;
                DrawLine(previous, current, color, duration);
                previous = current;
            }
        }

        private static void DrawCone(
            Vector3 center, Quaternion rotation, float radius, float angle,
            Color color, float duration)
        {
            if (radius <= 0f) return;
            float halfAngle = Mathf.Clamp(angle * 0.5f, 0f, 180f);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            float axial = Mathf.Cos(halfAngle * Mathf.Deg2Rad) * radius;
            float ringRadius = Mathf.Sin(halfAngle * Mathf.Deg2Rad) * radius;
            Vector3 capCenter = center + forward * axial;

            DrawCircle(capCenter, forward, ringRadius, color, duration);
            DrawLine(center, capCenter + right * ringRadius, color, duration);
            DrawLine(center, capCenter - right * ringRadius, color, duration);
            DrawLine(center, capCenter + up * ringRadius, color, duration);
            DrawLine(center, capCenter - up * ringRadius, color, duration);
        }

        private static void DrawBox(
            Vector3 center, Quaternion rotation, Vector3 size,
            Color color, float duration)
        {
            Vector3 half = size * 0.5f;
            for (int i = 0; i < BoxCorners.Length; i++)
            {
                Vector3 local = new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);
                BoxCorners[i] = center + rotation * local;
            }

            DrawBoxEdge(0, 1, color, duration);
            DrawBoxEdge(2, 3, color, duration);
            DrawBoxEdge(4, 5, color, duration);
            DrawBoxEdge(6, 7, color, duration);
            DrawBoxEdge(0, 2, color, duration);
            DrawBoxEdge(1, 3, color, duration);
            DrawBoxEdge(4, 6, color, duration);
            DrawBoxEdge(5, 7, color, duration);
            DrawBoxEdge(0, 4, color, duration);
            DrawBoxEdge(1, 5, color, duration);
            DrawBoxEdge(2, 6, color, duration);
            DrawBoxEdge(3, 7, color, duration);
        }

        private static void DrawBoxEdge(
            int a, int b, Color color, float duration) =>
            DrawLine(BoxCorners[a], BoxCorners[b], color, duration);

        private static void DrawCapsule(
            Vector3 center, Quaternion rotation, float radius, float length,
            Color color, float duration)
        {
            Vector3 end = center + rotation * Vector3.forward * length;
            Vector3 right = rotation * Vector3.right * radius;
            Vector3 up = rotation * Vector3.up * radius;
            DrawSphere(center, rotation, radius, color, duration);
            DrawSphere(end, rotation, radius, color, duration);
            DrawLine(center + right, end + right, color, duration);
            DrawLine(center - right, end - right, color, duration);
            DrawLine(center + up, end + up, color, duration);
            DrawLine(center - up, end - up, color, duration);
        }

        private static void DrawLine(
            Vector3 start, Vector3 end, Color color, float duration)
        {
            Debug.DrawLine(start, end, color, duration, false);
            Debug.DrawLine(
                start + s_lineRight, end + s_lineRight, color, duration, false);
            Debug.DrawLine(
                start - s_lineRight, end - s_lineRight, color, duration, false);
            Debug.DrawLine(
                start + s_lineUp, end + s_lineUp, color, duration, false);
            Debug.DrawLine(
                start - s_lineUp, end - s_lineUp, color, duration, false);
        }
    }

    public sealed class HitHandle
    {
        private readonly HashSet<IHittable> _struck = new HashSet<IHittable>();
        private float _elapsed;
        private float _nextRepeatTime;
        private bool _stopped;
        private bool _hasSampled;
        private readonly HitExecutionContext _context;

        internal HitData Definition { get; }
        internal Transform Source { get; }
        internal CombatTeam SourceTeam { get; }
        internal float PreviousRadius { get; set; }
        internal bool HasPreviousPose { get; private set; }
        internal Vector3 PreviousCenter { get; private set; }
        internal Quaternion PreviousRotation { get; private set; }
        internal bool DebugDraw => _context.DebugDraw;
        internal float DebugDuration => _context.DebugDuration;
        public bool HasSampled => _hasSampled;

        internal HitHandle(HitData definition, HitExecutionContext context)
        {
            Definition = definition;
            _context = context;
            Source = context.Source;
            SourceTeam = HitService.ResolveTeam(context.Source);
            PreviousRadius = 0f;
            _nextRepeatTime = definition.RepeatInterval;
        }

        public void Tick(float deltaTime, float normalizedProgress)
        {
            if (_stopped || Definition == null || Source == null) return;

            _elapsed += Mathf.Max(0f, deltaTime);
            bool firstSample = !_hasSampled;
            bool repeatSample = _hasSampled
                && Definition.Frequency == HitFrequency.RepeatInterval
                && _elapsed >= _nextRepeatTime;

            if (repeatSample)
            {
                _struck.Clear();
                _nextRepeatTime = _elapsed + Definition.RepeatInterval;
            }
            if (!HitService.Query(this, normalizedProgress)) return;
            _hasSampled = true;
            if (firstSample)
                _nextRepeatTime = _elapsed + Definition.RepeatInterval;
        }

        public void Stop()
        {
            _stopped = true;
            _struck.Clear();
        }

        internal bool HasStruck(IHittable target) => _struck.Contains(target);
        internal void MarkStruck(IHittable target) => _struck.Add(target);
        internal void SetPreviousPose(
            Vector3 center, Quaternion rotation, float radius)
        {
            PreviousCenter = center;
            PreviousRotation = rotation;
            PreviousRadius = radius;
            HasPreviousPose = true;
        }
        internal Transform ResolveOrigin() =>
            HitService.ResolveOrigin(Definition, _context);
    }
}
