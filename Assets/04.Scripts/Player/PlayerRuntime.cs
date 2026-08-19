using System;
using UnityEngine;
using ZZZ.Combat;
using ZZZ.Player.StateMachine;

namespace ZZZ.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SquadController))]
    public sealed class PlayerRuntime : MonoBehaviour
    {
        [Serializable]
        private sealed class HitLagSettings
        {
            [Tooltip("히트랙이 선형으로 복구되는 실제 시간(초)입니다.")]
            [SerializeField, Min(0f)] private float _duration = 0.08f;
            [Tooltip("X축은 히트랙 진행도, Y축은 실제 Time.timeScale 값입니다.")]
            [SerializeField] private AnimationCurve _gameSpeedCurve =
                AnimationCurve.Linear(0f, 0.1f, 1f, 1f);
            [Tooltip("X축은 히트랙 진행도, Y축은 공격 몬스터의 실제 추가 속도 배율입니다.")]
            [SerializeField] private AnimationCurve _monsterSpeedCurve =
                AnimationCurve.Linear(0f, 0f, 1f, 1f);

            public void Request(Transform source)
            {
                HitStopService.Request(
                    _duration, _gameSpeedCurve, source, _monsterSpeedCurve);
            }
        }

        [Header("Success Hit Lag")]
        [SerializeField] private HitLagSettings _parry = new HitLagSettings();
        [SerializeField] private HitLagSettings _perfectDodge = new HitLagSettings();

        private SquadController _squad;
        private PlayerActionController _activeActions;

        private void Awake()
        {
            _squad = GetComponent<SquadController>();
        }

        private void OnEnable()
        {
            _squad.OnActiveCharacterChanged += BindCharacter;
            BindCharacter(_squad.ActiveCharacter);
        }

        private void OnDisable()
        {
            _squad.OnActiveCharacterChanged -= BindCharacter;
            BindCharacter(null);
        }

        private void BindCharacter(PlayableCharacter character)
        {
            if (_activeActions != null)
            {
                _activeActions.ParrySucceeded -= OnParrySucceeded;
                _activeActions.PerfectDodgeSucceeded -= OnPerfectDodgeSucceeded;
            }

            _activeActions = character != null ? character.InputTarget : null;
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
