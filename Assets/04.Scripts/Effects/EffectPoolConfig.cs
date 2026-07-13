using UnityEngine;

namespace ZZZ.Effects
{
    // 이펙트 프리팹의 풀 용량(프리워밍 개수·상한). 풀은 프리팹 단위 전역 공유(EffectService)라
    // 이 값들은 프리팹의 속성(전역)이지 캐릭터별 설정이 아니다 — 같은 프리팹을 여러 캐릭터가 써도
    // 용량은 하나로 정의돼야 한다(예전엔 캐릭터마다 선언해 로드 순서로 MaxSize가 갈리던 문제 제거).
    // 캐릭터는 "이 프리팹을 쓴다"는 소유권만 선언하고(EffectOwnership이 config에서 유도), 개수/상한은 여기서 읽는다.
    // 이 컴포넌트를 안 붙인 프리팹은 온디맨드(프리웜 0·무제한)로 동작한다.
    [DisallowMultipleComponent]
    public class EffectPoolConfig : MonoBehaviour
    {
        [Tooltip("미리 만들어둘 인스턴스 수(첫 스폰 히칭/GC 방지). 0 = 프리웜 안 함(온디맨드).")]
        [Min(0)] public int PrewarmCount = 8;

        [Tooltip("풀 상한(동시 최대 인스턴스). 0 = 무제한. 초과분은 반납 시 파괴.")]
        [Min(0)] public int MaxSize = 0;
    }
}
