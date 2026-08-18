using System;
using UnityEngine;

namespace ZZZ
{
    // 링크 전이 '조건'의 추상 베이스 — TrackClip.Modules(SectionModule)와 동형 패턴.
    // ClipLink.Condition에 [SerializeReference]로 직렬화된다. 새 조건(거리/체력/AI결정 등) =
    // 이 클래스 상속 1개 추가 (ConfigState/에디터는 안 건드림). 평가에 필요한 질의는 ctx로 받는다.
    //
    // Timing(WhenMatched/OnRelease/OnEnd/OnEndIfMatched)과 윈도우[Start,End]는 ClipLink에 남는다 —
    // "언제 평가/발동할지"는 링크 소관, "무엇이 충족인지"만 조건 소관.
    [Serializable]
    public abstract class LinkCondition
    {
        // 일반 매칭 — WhenMatched 윈도우 / OnEnd 가드 / OnEndIfMatched 래치에서 쓰인다.
        public abstract bool Matches(ILinkConditionContext ctx);

        // OnRelease 타이밍 전용 — '발사 신호'(예: 차지 키를 뗌)가 충족됐는가. 입력형 조건만 의미를 갖는다.
        // 기본은 false(릴리스 개념 없음) → OnRelease 링크에 비입력 조건을 달면 발동하지 않는다.
        public virtual bool ReleaseTriggered(ILinkConditionContext ctx) => false;

        // 발동 직후 부수효과(입력 버퍼 소비 등). 입력을 요구한 조건만 구현. 기본 no-op.
        public virtual void Consume(ILinkConditionContext ctx) { }

        // 이 조건이 해당 공격 입력을 '받는' 조건인가 — 전역 폴백 트리거(강화 등)가 윈도우 전에
        // 입력을 가로채지 않도록 게이트하는 데 쓴다(ConfigState.ActiveSectionHandles). 입력형만 true.
        public virtual bool AcceptsInput(ComboInput input) => false;

        // 깊은 복사 (에디터 링크 복사/붙여넣기용) — 값 필드만 있는 조건은 멤버와이즈로 충분.
        public virtual LinkCondition Clone() => (LinkCondition)MemberwiseClone();

        // 인스펙터/에디터 표시용 — DisplayName은 값 포함 요약, MenuName은 추가 메뉴의 타입 이름.
        public virtual string DisplayName => GetType().Name;
        public virtual string MenuName    => GetType().Name;
    }

    // 조건 평가에 필요한 공통 런타임 질의 묶음 — 플레이어/몬스터가 각자 구현해 ConfigState에 주입한다.
    // 캐릭터 전용 질의는 이 인터페이스를 직접 늘리지 않고 파생 컨텍스트 인터페이스로 확장한다.
    public interface ILinkConditionContext
    {
        // ── 입력 버퍼 ──
        bool       HasBufferedInput { get; }
        ComboInput BufferedInput    { get; }
        bool       IsHeld(ComboInput input);
        void       ConsumeInput();

        // ── 이동/방향 ──
        MoveDir CurrentMoveDir { get; }   // 현재 이동 방향(절대 enum)
        Vector3 InputDir       { get; }   // 카메라 기준 월드 입력 방향 (Reverse 판정용)
        Vector3 Forward        { get; }   // 현재 facing (Reverse 판정용)
    }

    // 플레이어 입력 조건 — 기존 ClipLink의 Attack + Direction(+ RequireHeld)을 그대로 흡수.
    // 콤보 입력/방향/홀드차지(OnRelease)를 표현한다. (기존 ConfigState.AttackMatches/MoveConditionMatches 로직)
    [Serializable]
    public class InputCondition : LinkCondition
    {
        [Tooltip("요구 공격 입력 (None = 공격 없음, Any = 아무 공격)")]
        public ComboInput Attack = ComboInput.None;
        [Tooltip("요구 방향 입력 (Any = 상관없음, Reverse = 현재 진행 방향의 반대)")]
        public MoveDir Direction = MoveDir.Any;
        [Tooltip("true면 Attack 키가 '지금 눌려있을(held)' 때 충족 — 누름 버퍼 대신 홀드 상태를 본다(차지 루프)")]
        public bool RequireHeld = false;

        public override bool Matches(ILinkConditionContext ctx)
            => AttackMatches(ctx) && MoveMatches(ctx);

        // OnRelease: 이 조건의 Attack 키를 '뗀'(held 아님) 상태 + 방향 충족이면 발사.
        public override bool ReleaseTriggered(ILinkConditionContext ctx)
        {
            if (ctx.IsHeld(Attack)) return false;   // 아직 누르고 있음 → 차지 지속
            return MoveMatches(ctx);
        }

        // 실제 공격 입력을 요구한 조건만 입력 버퍼 소비 (같은 입력의 타 링크 오발동 방지).
        public override void Consume(ILinkConditionContext ctx)
        {
            if (Attack != ComboInput.None) ctx.ConsumeInput();
        }

        public override bool AcceptsInput(ComboInput input)
            => Attack == input || Attack == ComboInput.Any;

        private bool AttackMatches(ILinkConditionContext ctx)
        {
            // 홀드 조건 — None/Any는 홀드 개념이 없어 버퍼 폴백.
            if (RequireHeld && Attack != ComboInput.None && Attack != ComboInput.Any)
                return ctx.IsHeld(Attack);

            switch (Attack)
            {
                case ComboInput.None: return !ctx.HasBufferedInput;          // 공격 없을 때
                case ComboInput.Any:  return ctx.HasBufferedInput;           // 아무 공격
                default:              return ctx.HasBufferedInput            // 특정 공격
                                          && ctx.BufferedInput == Attack;
            }
        }

        // 방향 조건. Reverse는 카메라 절대 방향이 아니라 현재 facing과의 관계 → dot으로 별도 판정.
        private bool MoveMatches(ILinkConditionContext ctx)
        {
            if (Direction == MoveDir.Reverse)
            {
                Vector3 inputDir = ctx.InputDir;
                if (inputDir.sqrMagnitude < 0.0001f) return false;
                return Vector3.Dot(ctx.Forward, inputDir) < -0.707f;   // 입력이 진행방향의 반대(>135°)
            }
            switch (Direction)
            {
                case MoveDir.Any:    return true;
                case MoveDir.Moving: return ctx.CurrentMoveDir != MoveDir.Neutral;
                default:             return Direction == ctx.CurrentMoveDir;
            }
        }

        public override string DisplayName
        {
            get
            {
                string a = RequireHeld && Attack != ComboInput.None && Attack != ComboInput.Any
                    ? $"Hold {Attack}" : Attack.ToString();
                return Direction == MoveDir.Any ? a : $"{a}+{Direction}";
            }
        }
        public override string MenuName => "Input (Attack/Direction)";
    }

    // 무조건 충족 — 몬스터의 "끝나면 idle 복귀"(OnEnd) 같은 가드 없는 전이용.
    // (플레이어의 Attack=None은 '입력 없을 때'라 의미가 다르다 — 이건 진짜 항상 true.)
    [Serializable]
    public class AlwaysCondition : LinkCondition
    {
        public override bool Matches(ILinkConditionContext ctx) => true;
        public override string MenuName => "Always";
    }
}
