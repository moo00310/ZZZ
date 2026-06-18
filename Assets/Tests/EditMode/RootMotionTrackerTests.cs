using NUnit.Framework;
using UnityEngine;

namespace ZZZ.Tests
{
    /// <summary>
    /// RootMotionTracker.NextDelta 의 델타 판정 규칙 검증.
    /// Transform/씬 없이 순수 계산만 호출 → Play 없이 Test Runner 한 번으로 통과/실패 판정.
    /// </summary>
    public class RootMotionTrackerTests
    {
        // 케이스 1: 첫 샘플은 baseline만 잡고 이동 없음.
        [Test]
        public void 첫샘플은_델타가0이다()
        {
            var t = new RootMotionTracker();
            Vector3 d = t.NextDelta(0, 0.1f, new Vector3(0, 0, 1f), isLooping: false);
            AssertVec(Vector3.zero, d);
        }

        // 케이스 2: 같은 클립에서 시간이 전진하면 본 이동량 그대로.
        [Test]
        public void 같은클립_전진하면_본이동만큼_델타반환()
        {
            var t = new RootMotionTracker();
            t.NextDelta(0, 0.1f, new Vector3(0, 0, 1.0f), isLooping: false);   // baseline
            Vector3 d = t.NextDelta(0, 0.2f, new Vector3(0, 0, 1.5f), isLooping: false);
            AssertVec(new Vector3(0, 0, 0.5f), d);
        }

        // 케이스 3: 클립이 바뀌면 경계 델타를 버린다(순간이동 방지).
        [Test]
        public void 클립이바뀌면_델타를버린다()
        {
            var t = new RootMotionTracker();
            t.NextDelta(0, 0.9f, new Vector3(0, 0, 5f), isLooping: false);     // 클립0 끝, 본 멀리
            Vector3 d = t.NextDelta(1, 0.0f, new Vector3(0, 0, 0f), isLooping: false); // 클립1 시작
            AssertVec(Vector3.zero, d);   // -5 순간이동하면 버그
        }

        // ── 케이스 #4 ── 루프 클립이 끝(≈0.9)에서 처음(≈0.05)으로 되감기면 델타를 버린다.
        [Test]
        public void 루프클립이_되감기면_델타를버린다()
        {
            var t = new RootMotionTracker();
            t.NextDelta(0, 0.90f, new Vector3(0, 0, 2f), isLooping: true);     // 루프 끝 근처
            Vector3 d = t.NextDelta(0, 0.05f, new Vector3(0, 0, 0f), isLooping: true); // wrap
            AssertVec(Vector3.zero, d);   // baked 위치 2→0 뒤로점프 방지
        }

        // 케이스 5: 루프 클립이라도 시간이 전진 중이면 정상 적용(되감김만 버린다).
        [Test]
        public void 루프클립이라도_전진중이면_델타를적용한다()
        {
            var t = new RootMotionTracker();
            t.NextDelta(0, 0.40f, new Vector3(0, 0, 1.0f), isLooping: true);
            Vector3 d = t.NextDelta(0, 0.50f, new Vector3(0, 0, 1.3f), isLooping: true);
            Assert.AreEqual(0.3f, d.z, 1e-4f);
        }

        // 케이스 6: Reset 후 다음 샘플은 다시 baseline(델타 0) — 전이 직후 점프 방지.
        [Test]
        public void Reset후_다음샘플은_다시baseline이다()
        {
            var t = new RootMotionTracker();
            t.NextDelta(0, 0.1f, new Vector3(0, 0, 1f), isLooping: false);
            t.Reset();
            Vector3 d = t.NextDelta(0, 0.2f, new Vector3(0, 0, 9f), isLooping: false);
            AssertVec(Vector3.zero, d);   // Reset 했으니 8 점프 무시
        }

        // Vector3 정확비교(부동소수 오차 허용) — NUnit 기본 Equals 모호성 회피.
        private static void AssertVec(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-4f, "x");
            Assert.AreEqual(expected.y, actual.y, 1e-4f, "y");
            Assert.AreEqual(expected.z, actual.z, 1e-4f, "z");
        }
    }
}
