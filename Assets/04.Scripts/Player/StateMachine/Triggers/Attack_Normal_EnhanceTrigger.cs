using UnityEngine;
using ZZZ;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    // 강화 공격 트리거 (push) — 콤보 링크(Attack=Attack_Normal_Enhance)가 받지 못한 경우의 전역 폴백.
    // 콤보 중에는 각 공격 섹션의 Attack_Normal_Enhance 링크가 윈도우 안에서 입력을 소비해 진입하고(우선),
    // 걷기/Idle처럼 링크가 없는 상태에서는 이 트리거가 입력을 받아 강화 공격 config로 강제 진입한다.
    // 그래서 PlayerStateMachine은 _state.Update(콤보 링크 평가) 이후 입력이 남아 있을 때만 이걸 부른다.
    public class Attack_Normal_EnhanceTrigger
    {
        private readonly PlayerStateMachine _machine;
        private readonly ConfigState        _state;
        private readonly ConfigRegistry     _registry;
        private readonly InputBuffer        _input;
        private readonly string             _section;       // 진입 섹션 (특수 config의 EntrySection)
        private readonly float              _blend;
        private readonly float              _reinterrupt;   // 시전 중 재입력 무시 임계

        public Attack_Normal_EnhanceTrigger(PlayerStateMachine machine, ConfigState state, ConfigRegistry registry,
            InputBuffer input, string section, float blend, float reinterrupt)
        {
            _machine     = machine;
            _state       = state;
            _registry    = registry;
            _input       = input;
            _section     = section;
            _blend       = blend;
            _reinterrupt = reinterrupt;
        }

        public void Trigger()
        {
            var cfg = _registry.FindWithSection(_section);
            if (cfg == null)
            {
                Debug.LogWarning($"[Attack_Normal_Enhance] '{_section}' 섹션을 가진 config가 없음 — PlayerStateMachine 'Configs' 리스트와 섹션 이름 확인", _machine);
                return;
            }

            // 재진입 가드 — 이미 같은 스킬 시전 중이고 진행도가 낮으면 무시(연타로 무한 갱신 방지)
            if (_state.CurrentConfig == cfg && _state.ActiveSection == _section
                && _state.CurrentNormalizedTime < _reinterrupt)
            {
                _input.Consume();
                return;
            }

            _input.Consume();
            _state.InterruptWith(cfg, _section, _blend);
        }
    }
}
