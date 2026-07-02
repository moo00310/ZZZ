using System;
using System.Collections;
using UnityEngine;

namespace ZZZ.Effects
{
    // EffectService가 CompositeEffect의 StartDelay를 지연 실행하기 위해 쓰는 코루틴 호스트.
    // EffectService 자신은 static이라 코루틴을 못 돌리므로, 풀 루트 오브젝트에 붙여서 대신 돌린다.
    public class EffectServiceRunner : MonoBehaviour
    {
        public void Delay(float seconds, Action action)
        {
            if (seconds <= 0f) { action(); return; }
            StartCoroutine(DelayRoutine(seconds, action));
        }

        private IEnumerator DelayRoutine(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }
    }
}
