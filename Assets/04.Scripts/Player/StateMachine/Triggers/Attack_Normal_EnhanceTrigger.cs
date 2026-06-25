using UnityEngine;
using ZZZ;
using ZZZ.Combat;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    // 강화 공격 트리거 (push) — 콤보 링크(Attack=Attack_Normal_Enhance)가 받지 못한 경우의 전역 폴백.
    // 콤보 중에는 각 공격 섹션의 Attack_Normal_Enhance 링크가 윈도우 안에서 입력을 소비해 진입하고(우선),
    // 걷기/Idle처럼 링크가 없는 상태에서는 이 트리거가 입력을 받아 강화 공격 config로 강제 진입한다.
    // 그래서 PlayerStateMachine은 _state.Update(콤보 링크 평가) 이후 입력이 남아 있을 때만 이걸 부른다.
    //
    // 섹션 선택 — 방향 우선, 그다음 거리.
    //   이동 입력 Forward(W) → 앞 섹션 / Back(S) → 뒤 섹션 (전용 모션: 전진 돌진 / 백스텝 카운터 등).
    //   그 외(Neutral·Left·Right) → 전방 적과의 거리로 근/중/원 분기 (근접 강타 / 원거리 대시).
    //   방향 전용 섹션이 아직 config에 없으면 거리 분기로 폴백, 거리 변종이 없으면 가까운 단계로 폴백.
    //
    // 설정은 이 객체가 직접 들고, 런타임 의존은 Init으로 주입한다.
    [System.Serializable]
    public class Attack_Normal_EnhanceTrigger
    {
        [Header("진입 섹션 — 방향 우선(앞W/뒤S), 중립이면 거리(근/중/원)")]
        [Tooltip("앞방향(W)+E 진입 섹션")]
        [SerializeField] private string _sectionForward = "Attack_Normal_Enhance_Front_01";
        [Tooltip("뒷방향(S)+E 진입 섹션")]
        [SerializeField] private string _sectionBack    = "Attack_Normal_Enhance_Back_01";
        [Tooltip("근접(기본·중립) 진입 섹션")]
        [SerializeField] private string _sectionNear    = "Attack_Normal_Enhance_01";
        [Tooltip("중거리 진입 섹션")]
        [SerializeField] private string _sectionMid     = "Attack_Normal_Enhance_02";
        [Tooltip("원거리 진입 섹션")]
        [SerializeField] private string _sectionFar     = "Attack_Normal_Enhance_03";

        [Header("거리 임계 / 전이")]
        [Tooltip("이 거리 이상 → 중거리 섹션")]
        [SerializeField] private float _midDist = 2f;
        [Tooltip("이 거리 이상 → 원거리 섹션")]
        [SerializeField] private float _farDist = 4f;
        [SerializeField, Range(0f, 0.2f)] private float _blend = 0.05f;
        [Tooltip("시전 중 재입력 무시 임계")]
        [SerializeField, Range(0f, 1f)] private float _reinterrupt = 0.3f;

        // ── 런타임 의존 (직렬화 안 함, Init으로 주입) ──
        private PlayerStateMachine _machine;
        private ConfigState        _state;
        private ConfigRegistry     _registry;
        private InputBuffer        _input;
        private EnemySensor        _sensor;

        public void Init(PlayerStateMachine machine, ConfigState state, ConfigRegistry registry,
            InputBuffer input, EnemySensor sensor)
        {
            _machine  = machine;
            _state    = state;
            _registry = registry;
            _input    = input;
            _sensor   = sensor;
        }

        public void Trigger()
        {
            string section = PickSection();
            var    cfg     = _registry.FindWithSection(section);
            if (cfg == null)
            {
                Debug.LogWarning($"[Attack_Normal_Enhance] '{section}' 섹션을 가진 config가 없음 — PlayerStateMachine 'Configs' 리스트와 섹션 이름 확인", _machine);
                return;
            }

            // 재진입 가드 — 이미 같은 스킬 시전 중이고 진행도가 낮으면 무시(연타로 무한 갱신 방지)
            if (_state.CurrentConfig == cfg && _state.ActiveSection == section
                && _state.CurrentNormalizedTime < _reinterrupt)
            {
                _input.Consume();
                return;
            }

            _input.Consume();
            _state.InterruptWith(cfg, section, _blend);
        }

        // 방향 우선 → 중립이면 거리. 이동 입력 Forward/Back은 전용 섹션, 그 외는 거리 분기로.
        // 방향 전용 섹션이 config에 없으면 거리 분기로 폴백.
        private string PickSection()
        {
            switch (_machine.CurrentMoveDir)
            {
                case MoveDir.Forward: return Resolve(_sectionForward) ?? PickSectionByDistance();
                case MoveDir.Back:    return Resolve(_sectionBack)    ?? PickSectionByDistance();
                default:              return PickSectionByDistance();   // Neutral·Left·Right → 거리
            }
        }

        // 전방 적과의 수평 거리로 근/중/원 섹션 선택. 적이 없으면 근접.
        // 변종 섹션이 아직 config에 없으면 한 단계 가까운 쪽으로 폴백(원→중→근).
        private string PickSectionByDistance()
        {
            if (_sensor == null) return _sectionNear;
            if (_sensor.FindTarget(out float dist) == null) return _sectionNear;   // 적 없음 → 근접

            if (dist >= _farDist) return Resolve(_sectionFar) ?? Resolve(_sectionMid) ?? _sectionNear;
            if (dist >= _midDist) return Resolve(_sectionMid) ?? _sectionNear;
            return _sectionNear;
        }

        // config에 실제로 존재하는 섹션이면 그대로, 아니면 null (폴백 신호)
        private string Resolve(string section)
            => !string.IsNullOrEmpty(section) && _registry.FindWithSection(section) != null ? section : null;
    }
}
