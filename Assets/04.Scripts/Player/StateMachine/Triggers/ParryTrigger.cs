using UnityEngine;
using ZZZ;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    // 패링 트리거 (push) — 어떤 config에 있든 패링 입력 시 패링 스탠스(ParryAid_Start)로 강제 진입.
    // 회피(DodgeTrigger)와 같은 push 방식이지만 방향 분기는 없다(스탠스 단일 진입).
    // 스탠스 진입 후 ParryModule이 활성 윈도우 동안 ParryActive를 켜고, 그 사이 적 공격이 닿으면
    // HitTrigger가 쳐냄(ParryAid_L/H)으로 분기한다 — 판정 자체는 HitTrigger/ParryModule 담당.
    [System.Serializable]
    public class ParryTrigger
    {
        [Tooltip("섹션 접두어 (Attack_ParryAid_ → +Start/L/H)")]
        [SerializeField] private string _prefix = "Attack_ParryAid_";
        [SerializeField, Range(0f, 0.2f)] private float _blend = 0.05f;
        [Tooltip("스탠스 중 재입력 무시 임계")]
        [SerializeField, Range(0f, 1f)] private float _reinterrupt = 0.3f;

        // 쳐냄 섹션(+L/H)에 같은 접두어를 쓰는 HitTrigger가 참조한다.
        public string Prefix => _prefix;

        // ── 런타임 의존 (직렬화 안 함, Init으로 주입) ──
        private PlayerActionController _controller;
        private ConfigState        _state;
        private ConfigRegistry     _registry;
        private InputBuffer        _input;

        public void Init(PlayerActionController controller, ConfigState state,
            ConfigRegistry registry, InputBuffer input)
        {
            _controller = controller;
            _state    = state;
            _registry = registry;
            _input    = input;
        }

        public void Trigger()
        {
            if (IsHitReaction(_state.ActiveSection))
            {
                _input.Consume();
                return;
            }

            string section = _prefix + "Start";   // Attack_ParryAid_ + Start
            var cfg = _registry.FindWithSection(section);
            if (cfg == null)
            {
                Debug.LogWarning($"[Parry] '{section}' 섹션을 가진 config가 없음 — PlayerActionController 'Configs' 리스트와 섹션 이름 확인", _controller);
                return;
            }

            // 재진입 가드 — 이미 같은 스탠스 중이고 진행도가 낮으면 무시(연타로 스탠스 무한 갱신 방지)
            if (_state.CurrentConfig == cfg && _state.ActiveSection == section
                && _state.CurrentNormalizedTime < _reinterrupt)
            {
                _input.Consume();
                return;
            }

            _input.Consume();
            _state.InterruptWith(cfg, section, _blend);
        }

        private static bool IsHitReaction(string section)
        {
            return !string.IsNullOrEmpty(section)
                && section.StartsWith("Hit_", System.StringComparison.Ordinal);
        }
    }
}
