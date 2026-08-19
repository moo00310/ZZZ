using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Combat
{
    public enum HitResult
    {
        Accepted,
        Ignored
    }

    public readonly struct HitContext
    {
        public HitData Definition { get; }
        public Transform Source { get; }
        public CombatTeam SourceTeam { get; }
        public Vector3 HitPoint { get; }
        public Vector3 Direction { get; }

        public HitContext(HitData definition, Transform source,
            CombatTeam sourceTeam, Vector3 hitPoint, Vector3 direction)
        {
            Definition = definition;
            Source = source;
            SourceTeam = sourceTeam;
            HitPoint = hitPoint;
            Direction = direction;
        }
    }

    public readonly struct HitExecutionContext
    {
        public Transform Source { get; }
        public Transform EffectOrigin { get; }
        public IHitOriginResolver OriginResolver { get; }
        public bool DebugDraw { get; }
        public float DebugDuration { get; }

        public HitExecutionContext(Transform source, Transform effectOrigin = null,
            IHitOriginResolver originResolver = null, bool debugDraw = false,
            float debugDuration = 0.1f)
        {
            Source = source;
            EffectOrigin = effectOrigin;
            OriginResolver = originResolver;
            DebugDraw = debugDraw;
            DebugDuration = Mathf.Max(0f, debugDuration);
        }
    }

    public interface IHitOriginResolver
    {
        bool TryResolve(string key, out Transform origin);
    }

    public interface IHitSource
    {
        CombatTeam Team { get; }
    }

    public interface IParryWarningReceiver
    {
        void ReceiveParryWarning(in HitContext context, float duration);
        void ReceiveParryImpact(in HitContext context);
    }

    public interface IHitLagTarget
    {
        float HitLagSpeed { get; }
        void SetHitLagSpeed(float speed);
    }

    public static class HitStopService
    {
        private static HitStopRunner s_runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            s_runner = null;
        }

        public static void Request(float duration,
            AnimationCurve gameSpeedCurve, Transform source,
            AnimationCurve monsterSpeedCurve)
        {
            if (duration <= 0f) return;
            GetRunner().Request(
                duration, gameSpeedCurve, source, monsterSpeedCurve);
        }

        private static HitStopRunner GetRunner()
        {
            if (s_runner != null) return s_runner;

            var gameObject = new GameObject("HitStopService");
            Object.DontDestroyOnLoad(gameObject);
            s_runner = gameObject.AddComponent<HitStopRunner>();
            return s_runner;
        }
    }

    internal sealed class HitStopRunner : MonoBehaviour
    {
        private bool _isActive;
        private float _restoreTimeScale;
        private float _appliedTimeScale;
        private float _startRealtime;
        private float _endRealtime;
        private AnimationCurve _gameSpeedCurve;
        private readonly List<TargetHitLag> _targets = new List<TargetHitLag>();

        private sealed class TargetHitLag
        {
            public IHitLagTarget Target;
            public float RestoreSpeed;
            public float AppliedSpeed;
            public float StartRealtime;
            public float EndRealtime;
            public AnimationCurve SpeedCurve;
        }

        internal void Request(float duration,
            AnimationCurve gameSpeedCurve, Transform source,
            AnimationCurve monsterSpeedCurve)
        {
            float now = Time.realtimeSinceStartup;
            if (!_isActive)
            {
                _isActive = true;
                _restoreTimeScale = Time.timeScale;
            }

            _startRealtime = now;
            _endRealtime = Mathf.Max(
                _endRealtime, now + duration);
            _gameSpeedCurve = gameSpeedCurve;
            _appliedTimeScale = EvaluateSpeed(
                _gameSpeedCurve, 0f, _restoreTimeScale);
            Time.timeScale = _appliedTimeScale;
            ApplyTargetHitLag(
                source, duration, monsterSpeedCurve);
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                TargetHitLag active = _targets[i];
                if (!IsTargetAlive(active.Target))
                {
                    _targets.RemoveAt(i);
                    continue;
                }

                if (now >= active.EndRealtime)
                {
                    RestoreTarget(active);
                    _targets.RemoveAt(i);
                    continue;
                }

                float progress = Mathf.InverseLerp(
                    active.StartRealtime, active.EndRealtime, now);
                active.AppliedSpeed = EvaluateSpeed(
                    active.SpeedCurve, progress, active.RestoreSpeed);
                active.Target.SetHitLagSpeed(active.AppliedSpeed);
            }

            if (!_isActive) return;
            if (now >= _endRealtime)
            {
                RestoreTimeScale();
                return;
            }

            float gameProgress = Mathf.InverseLerp(
                _startRealtime, _endRealtime, now);
            _appliedTimeScale = EvaluateSpeed(
                _gameSpeedCurve, gameProgress, _restoreTimeScale);
            Time.timeScale = _appliedTimeScale;
        }

        private void OnDestroy()
        {
            RestoreTimeScale();
            for (int i = _targets.Count - 1; i >= 0; i--)
                RestoreTarget(_targets[i]);
            _targets.Clear();
        }

        private void ApplyTargetHitLag(
            Transform source, float duration, AnimationCurve speedCurve)
        {
            if (source == null) return;
            IHitLagTarget target = source.GetComponentInParent<IHitLagTarget>();
            if (target == null) return;

            float endRealtime = Time.realtimeSinceStartup + duration;
            for (int i = 0; i < _targets.Count; i++)
            {
                TargetHitLag active = _targets[i];
                if (!ReferenceEquals(active.Target, target)) continue;

                active.AppliedSpeed = EvaluateSpeed(
                    speedCurve, 0f, active.RestoreSpeed);
                active.StartRealtime = Time.realtimeSinceStartup;
                active.EndRealtime = Mathf.Max(active.EndRealtime, endRealtime);
                active.SpeedCurve = speedCurve;
                target.SetHitLagSpeed(active.AppliedSpeed);
                return;
            }

            var added = new TargetHitLag
            {
                Target = target,
                RestoreSpeed = target.HitLagSpeed,
                AppliedSpeed = EvaluateSpeed(
                    speedCurve, 0f, target.HitLagSpeed),
                StartRealtime = Time.realtimeSinceStartup,
                EndRealtime = endRealtime,
                SpeedCurve = speedCurve,
            };
            _targets.Add(added);
            target.SetHitLagSpeed(added.AppliedSpeed);
        }

        private static void RestoreTarget(TargetHitLag active)
        {
            if (!IsTargetAlive(active.Target)) return;
            if (Mathf.Approximately(
                    active.Target.HitLagSpeed, active.AppliedSpeed))
                active.Target.SetHitLagSpeed(active.RestoreSpeed);
        }

        private static bool IsTargetAlive(IHitLagTarget target)
        {
            if (target == null) return false;
            return !(target is Object targetObject) || targetObject != null;
        }

        private static float EvaluateSpeed(
            AnimationCurve speedCurve, float progress, float fallback)
        {
            if (speedCurve == null || speedCurve.length == 0)
                return Mathf.Max(0f, fallback);
            return Mathf.Max(0f, speedCurve.Evaluate(progress));
        }

        private void RestoreTimeScale()
        {
            if (!_isActive) return;
            if (Mathf.Approximately(Time.timeScale, _appliedTimeScale))
                Time.timeScale = _restoreTimeScale;
            _isActive = false;
        }
    }
}
