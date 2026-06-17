using System.Collections;
using UnityEngine;

namespace ZZZ.Player.StateMachine
{
    // 애니메이터 얇은 래퍼 — config가 클립을 이름으로 재생(Play)하고, 피격 흔들림(additive)을 구동한다.
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimatorBridge : MonoBehaviour
    {
        [Header("Additive Hit Shake (layer 1)")]
        [SerializeField] private int   _additiveLayer = 1;
        [SerializeField] private float _shakeFade     = 0.15f;  // 흔들림이 0으로 사라지기까지 시간(초)

        // Additive 흔들림 클립 — Animator Controller의 layer 1 State 이름과 정확히 일치해야 함
        public const string HitShake = "Avatar_Female_Size02_Burnice_Ani_Hit_Shake";

        private Animator  _animator;
        private Coroutine _shakeRoutine;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            ApplyAnimatorSpeed();
        }

        // 비주얼 재생 속도 — config의 Speed와 로직 타임라인을 일치시킨다.
        public void ApplyAnimatorSpeed(float speed = 1f) => _animator.speed = speed;

        // 클립을 layer 0에 CrossFade. crossFade는 "초" 단위(고정 시간)라 config의 BlendDuration(초)과 단위가 일치.
        public void Play(string stateName, float crossFade = 0.01f)
            => _animator.CrossFadeInFixedTime(stateName, crossFade, 0);

        // ── Hit Shake (Additive) ──────────────────────────────────
        // Hit config 클립의 Notify(EventName="OnHitShake")가 SendMessage로 호출한다.
        // additive 레이어에 Hit_Shake를 재생하고 끝나면 weight를 0으로 페이드.
        public void OnHitShake()
        {
            if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
            _shakeRoutine = StartCoroutine(ShakeRoutine());
        }

        private IEnumerator ShakeRoutine()
        {
            _animator.CrossFadeInFixedTime(HitShake, 0.05f, _additiveLayer);
            _animator.SetLayerWeight(_additiveLayer, 1f);

            // Hit_Shake 클립 길이만큼 weight 1 유지 → 그 뒤 _shakeFade 동안 0으로 감쇠
            float hold = _animator.GetCurrentAnimatorStateInfo(_additiveLayer).length;
            if (hold <= 0.01f) hold = 0.4f;   // 길이 못 읽으면 기본값
            yield return new WaitForSeconds(Mathf.Max(0f, hold - _shakeFade));

            float t = 0f;
            while (t < _shakeFade)
            {
                t += Time.deltaTime;
                _animator.SetLayerWeight(_additiveLayer, 1f - Mathf.Clamp01(t / _shakeFade));
                yield return null;
            }
            _animator.SetLayerWeight(_additiveLayer, 0f);
            _shakeRoutine = null;
        }
    }
}
