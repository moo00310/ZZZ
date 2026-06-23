using UnityEngine;
using ZZZ;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    // 회피 트리거 (push) — 어떤 config에 있든 회피 입력 시 강제 진입. 콤보보다 우선(공격 중 캔슬).
    // Hit과 같은 방식: 입력 상태로 섹션 이름(접두어+방향)을 만들고 registry에서 검색해 진입한다.
    public class DodgeTrigger
    {
        private readonly PlayerStateMachine _machine;
        private readonly ConfigState        _state;
        private readonly ConfigRegistry     _registry;
        private readonly InputBuffer        _input;
        private readonly PlayerResources    _resources;     // 대쉬 충전/쿨 게이트 (null이면 게이트 없음)
        private readonly string             _prefix;        // 섹션 접두어 (Evade_Front 등)
        private readonly float              _blend;
        private readonly float              _reinterrupt;   // 회피 중 재입력 무시 임계

        public DodgeTrigger(PlayerStateMachine machine, ConfigState state, ConfigRegistry registry,
            InputBuffer input, PlayerResources resources, string prefix, float blend, float reinterrupt)
        {
            _machine     = machine;
            _state       = state;
            _registry    = registry;
            _input       = input;
            _resources   = resources;
            _prefix      = prefix;
            _blend       = blend;
            _reinterrupt = reinterrupt;
        }

        public void Trigger()
        {
            // 자원/쿨 게이트 — 충전이 없으면 회피 불가 (resources 없으면 게이트 생략)
            // 차지 부족 시 버퍼된 dodge를 비운다. 안 그러면 이 입력이 Attack=None 링크
            // (Run_Loop/Idle 복귀)를 막아 회피 끝에서 멈칫한다 — "입력 없음"과 동일하게 취급해 넘긴다.
            if (_resources != null && !_resources.CanDash())
            {
                _input.Consume();
                return;
            }

            string section = _prefix + Suffix();
            var cfg = _registry.FindWithSection(section);
            if (cfg == null)
            {
                Debug.LogWarning($"[Dodge] '{section}' 섹션을 가진 config가 없음 — PlayerStateMachine 'Configs' 리스트와 섹션 이름 확인", _machine);
                return;
            }

            // 재진입 가드 — 이미 회피 중이고 진행도가 낮으면 무시(연타 프리즈 방지)
            if (_state.CurrentConfig == cfg && _state.CurrentNormalizedTime < _reinterrupt)
                return;

            _input.Consume();
            _resources?.NotifyDash();   // 충전 1 소비
            _state.InterruptWith(cfg, section, _blend);
        }

        // 회피 섹션 선택:
        //   무입력(Neutral)     → Back  (제자리 백스텝, 회전 없음)
        //   방향(W/A/S/D) 일반  → Front (진입 스냅으로 입력 방향 재조준 후 전진 — Back 입력도 그쪽으로 돌아 전진)
        //   방향 + 퍼펙트 타이밍 → Left/Right (좌우 회피 모션 — 입력 좌=Left, 그 외=Right)
        private string Suffix()
        {
            MoveDir d = _state.CurrentMoveDir;

            // 입력이 없을 때만 제자리 백스텝 — 직전이 Back이면 Back_02로 교대(단조로움 방지).
            if (d == MoveDir.Neutral)
                return _state.ActiveSection == _prefix + "Back" ? "Back_02" : "Back";

            // 방향 입력은 뒤(↓)를 포함해 모두 입력 방향으로 재조준 후 전진 회피.
            // → 카메라 뒤를 보던 중 ↓ 입력 시 그쪽으로 돌아 전진한다(백스텝으로 빠지지 않음).
            if (_machine.IncomingAttackActive)             // 적 공격 윈도우 안 → 퍼펙트
                return d == MoveDir.Left ? "Left" : "Right";

            return "Front";
        }
    }
}
