using UnityEngine;

namespace ZZZ
{
    /// <summary>
    /// 베이크된 루트본의 로컬 위치를 프레임마다 받아, "이번 프레임에 적용할 로컬 이동 델타"를 계산한다.
    /// 클립 전환(다른 클립으로 점프)과 루프 되감기(normalizedTime 감소)에서는 델타를 버려
    /// 캐릭터가 순간이동하는 것을 막는다.
    ///
    /// Transform·씬·AnimationMode에 의존하지 않는 순수 계산만 담당 → 단위 테스트 가능.
    /// 월드 변환(TransformDirection)과 스케일 적용은 호출자(루트본/transform을 아는 쪽)가 담당한다.
    /// </summary>
    // TODO(몬스터 루트모션): 현재 이 코어(flush/경계→cur-prev)가 PlayerController.ComputeRootDeltaLocal에
    // 런타임용으로 한 벌 더 복제돼 있다(테스트는 이 struct만 검증). 몬스터 루트모션을 붙일 때 양쪽을
    // 이 헬퍼로 통합해 플레이어·몬스터·에디터 프리뷰가 한 소스를 공유하게 할 것. — [[todo-rootmotion-tracker-unify]]
    public struct RootMotionTracker
    {
        public bool    HasPrev;       // 직전 샘플 유효 여부 (false면 델타 0)
        public int     PrevClipIdx;   // 직전 샘플이 속한 클립
        public float   PrevClipTime;  // 직전 샘플의 클립 로컬 시간(초)
        public Vector3 PrevLocalPos;  // 직전 루트본 로컬 위치

        /// 전이/재시작 시 호출 — 다음 샘플을 새 baseline으로 삼아 경계 델타를 버린다.
        public void Reset() => HasPrev = false;

        /// <summary>
        /// 이번 샘플을 받아 적용할 로컬 델타를 반환한다.
        /// 첫 샘플·클립 전환·루프 되감기에서는 <see cref="Vector3.zero"/>.
        /// </summary>
        public Vector3 NextDelta(int clipIdx, float clipTime, Vector3 curLocal, bool isLooping)
        {
            bool sameClip = PrevClipIdx == clipIdx;
            bool wrapped  = isLooping && clipTime < PrevClipTime - 0.0001f;   // 루프 되감김
            bool valid    = HasPrev && sameClip && !wrapped;

            Vector3 delta = valid ? curLocal - PrevLocalPos : Vector3.zero;

            PrevLocalPos = curLocal;
            PrevClipTime = clipTime;
            PrevClipIdx  = clipIdx;
            HasPrev      = true;
            return delta;
        }
    }
}
