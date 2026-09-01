using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace ZZZ.Agent
{
    // 플레이어 자원/쿨타임 단일 관리자 — 대쉬 충전, (예정)스킬·궁극기 게이지.
    // 트리거(DodgeTrigger 등)는 여기에 "쓸 수 있나"만 묻고, 사용 시 통지한다.
    [MovedFrom(true, "ZZZ.Player", "Assembly-CSharp", "PlayerResources")]
    public class AgentResources : MonoBehaviour
    {
        [Header("Dash — 충전(charge) 방식")]
        [SerializeField] private int   _dashMaxCharges   = 2;     // 최대 충전 수 (연속 회피 가능 횟수)
        [SerializeField] private float _dashRechargeTime = 1.5f;  // 충전 1개 회복에 걸리는 시간(초)

        // 0 ~ _dashMaxCharges. 소수부 = 다음 충전이 회복되는 중.
        private float _dashCharges;

        // ── 읽기 전용 노출 (HUD/디버그) ──
        public float DashCharges    => _dashCharges;
        public int   DashMaxCharges => _dashMaxCharges;

        private void Awake() => _dashCharges = _dashMaxCharges;

        private void Update()
        {
            if (_dashCharges < _dashMaxCharges && _dashRechargeTime > 0f)
                _dashCharges = Mathf.Min(_dashMaxCharges,
                    _dashCharges + Time.deltaTime / _dashRechargeTime);
        }

        // ── Dash ──────────────────────────────────────────────────
        public bool CanDash() => _dashCharges >= 1f;

        // 회피 1회 소비 — Trigger가 실제 진입을 확정한 뒤 호출.
        public void NotifyDash() => _dashCharges = Mathf.Max(0f, _dashCharges - 1f);

        // TODO: 스킬 게이지 / 궁극기 자원 — 소비처가 생기면 같은 패턴으로 추가
        //   public bool TryConsumeSkill(float cost) { ... }
        //   public bool UltimateReady => _ultGauge >= _ultMax;
    }
}
