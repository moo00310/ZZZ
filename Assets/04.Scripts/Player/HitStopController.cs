using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using ZZZ.Agent;
using ZZZ.Combat;

namespace ZZZ.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SquadController))]
    [MovedFrom(true, "ZZZ.Player", "Assembly-CSharp", "PlayerRuntime")]
    public sealed class HitStopController : MonoBehaviour
    {
        [Serializable]
        private sealed class HitStopSettings
        {
            [Tooltip("히트스톱이 복구되는 실제 시간(초)입니다.")]
            [SerializeField, Min(0f)] private float _duration = 0.08f;
            [Tooltip("X축은 히트스톱 진행도, Y축은 실제 Time.timeScale 값입니다.")]
            [SerializeField] private AnimationCurve _gameSpeedCurve =
                AnimationCurve.Linear(0f, 0.1f, 1f, 1f);
            [Tooltip("X축은 히트스톱 진행도, Y축은 공격 몬스터의 실제 추가 속도 배율입니다.")]
            [SerializeField] private AnimationCurve _monsterSpeedCurve =
                AnimationCurve.Linear(0f, 0f, 1f, 1f);

            public void Request(Transform source)
            {
                HitStopService.Request(
                    _duration, _gameSpeedCurve, source, _monsterSpeedCurve);
            }
        }

        [Header("Success Hit Stop")]
        [SerializeField] private HitStopSettings _parry = new HitStopSettings();
        [SerializeField] private HitStopSettings _perfectDodge = new HitStopSettings();

        private SquadController _squad;
        private AgentActionController _activeActions;

        private void Awake()
        {
            _squad = GetComponent<SquadController>();
        }

        private void OnEnable()
        {
            _squad.OnActiveAgentChanged += BindAgent;
            BindAgent(_squad.ActiveAgent);
        }

        private void OnDisable()
        {
            _squad.OnActiveAgentChanged -= BindAgent;
            BindAgent(null);
        }

        private void BindAgent(AgentRoot agent)
        {
            if (_activeActions != null)
            {
                _activeActions.ParrySucceeded -= OnParrySucceeded;
                _activeActions.PerfectDodgeSucceeded -= OnPerfectDodgeSucceeded;
            }

            _activeActions = agent != null ? agent.InputTarget : null;
            if (_activeActions == null) return;

            _activeActions.ParrySucceeded += OnParrySucceeded;
            _activeActions.PerfectDodgeSucceeded += OnPerfectDodgeSucceeded;
        }

        private void OnParrySucceeded(HitContext context)
        {
            _parry.Request(context.Source);
        }

        private void OnPerfectDodgeSucceeded(Transform source)
        {
            _perfectDodge.Request(source);
        }
    }
}
