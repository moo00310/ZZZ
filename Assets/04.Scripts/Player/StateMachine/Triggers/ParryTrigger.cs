using UnityEngine;
using ZZZ;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    // 패링 트리거 (push) — 어떤 config에 있든 패링 입력 시 패링 스탠스(ParryAid_Start)로 강제 진입.
    // 회피(DodgeTrigger)와 같은 push 방식이지만 방향 분기는 없다(스탠스 단일 진입).
    // 스탠스 진입 후 ParryModule이 활성 윈도우 동안 ParryActive를 켜고, 그 사이 적 공격이 닿으면
    // HitTrigger가 쳐냄(ParryAid_L/H)으로 분기한다 — 판정 자체는 HitTrigger/ParryModule 담당.
    public class ParryTrigger
    {
        private readonly PlayerStateMachine _machine;
        private readonly ConfigState        _state;
        private readonly ConfigRegistry     _registry;
        private readonly InputBuffer        _input;
        private readonly string             _section;       // 진입 섹션 (ParryAid_Start)
        private readonly float              _blend;
        private readonly float              _reinterrupt;   // 스탠스 중 재입력 무시 임계

        public ParryTrigger(PlayerStateMachine machine, ConfigState state, ConfigRegistry registry,
            InputBuffer input, string prefix, float blend, float reinterrupt)
        {
            _machine     = machine;
            _state       = state;
            _registry    = registry;
            _input       = input;
            _section     = prefix + "Start";   // Attack_ParryAid_ + Start
            _blend       = blend;
            _reinterrupt = reinterrupt;
        }

        public void Trigger()
        {
            var cfg = _registry.FindWithSection(_section);
            if (cfg == null)
            {
                Debug.LogWarning($"[Parry] '{_section}' 섹션을 가진 config가 없음 — PlayerStateMachine 'Configs' 리스트와 섹션 이름 확인", _machine);
                return;
            }

            // 재진입 가드 — 이미 같은 스탠스 중이고 진행도가 낮으면 무시(연타로 스탠스 무한 갱신 방지)
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
