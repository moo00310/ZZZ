using System.Collections;
using UnityEngine;

namespace ZZZ.Player.StateMachine
{
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimatorBridge : MonoBehaviour
    {
        [Header("Additive Hit Shake (layer 1)")]
        [SerializeField] private int   _additiveLayer = 1;
        [SerializeField] private float _shakeFade     = 0.15f;  // 흔들림이 0으로 사라지기까지 시간(초)

        private Animator  _animator;
        private Coroutine _shakeRoutine;

        // ── Clip Name Constants ───────────────────────────────────
        public const string Idle      = "Avatar_Female_Size02_Burnice_Ani_Idle";
        public const string IdleLoop  = "Avatar_Female_Size02_Burnice_Ani_Idle_AFK";
        public const string WalkStart = "Avatar_Female_Size02_Burnice_Ani_Walk_Start";
        public const string WalkLoop  = "Avatar_Female_Size02_Burnice_Ani_Walk_Loop";
        public const string WalkEnd   = "Avatar_Female_Size02_Burnice_Ani_Walk_End";
        public const string RunLoop   = "Avatar_Female_Size02_Burnice_Ani_Run_Loop";
        public const string RunEnd    = "Avatar_Female_Size02_Burnice_Ani_Run_End";
        public const string TurnBack  = "Avatar_Female_Size02_Burnice_Ani_Turn_Back";
        public const string Taunt     = "Avatar_Female_Size02_Burnice_Ani_Taunt";

        // Attack
        public const string AttackNormal_01    = "Avatar_Female_Size02_Burnice_Ani_Attack_Normal_01";
        public const string AttackNormal_02    = "Avatar_Female_Size02_Burnice_Ani_Attack_Normal_02";
        public const string AttackNormal_03    = "Avatar_Female_Size02_Burnice_Ani_Attack_Normal_03";
        public const string AttackNormal_04    = "Avatar_Female_Size02_Burnice_Ani_Attack_Normal_04";
        public const string AttackNormal_05    = "Avatar_Female_Size02_Burnice_Ani_Attack_Normal_05";
        public const string AttackEnhance_01   = "Avatar_Female_Size02_Burnice_Ani_Attack_Normal_Enhance_01";
        public const string AttackEnhance_02   = "Avatar_Female_Size02_Burnice_Ani_Attack_Normal_Enhance_02";
        public const string AttackEnhance_03   = "Avatar_Female_Size02_Burnice_Ani_Attack_Normal_Enhance_03";
        public const string AttackRush         = "Avatar_Female_Size02_Burnice_Ani_Attack_Rush";
        public const string AttackSpecial      = "Avatar_Female_Size02_Burnice_Ani_Attack_Special_01";
        public const string AttackExSpecial_01 = "Avatar_Female_Size02_Burnice_Ani_Attack_ExSpecial_01";
        public const string AttackExSpecial_02 = "Avatar_Female_Size02_Burnice_Ani_Attack_ExSpecial_02";
        public const string AttackCounter      = "Avatar_Female_Size02_Burnice_Ani_Attack_Counter_01";
        public const string AttackParryStart   = "Avatar_Female_Size02_Burnice_Ani_Attack_ParryAid_Start";

        // Hit (피격) — 아래 두 이름은 Animator Controller의 State 이름과 정확히 일치해야 함.
        //   HitFront : layer 0 (base 모션, walk/콤보를 덮어씀)
        //   HitShake : layer 1 (Additive blending 레이어의 State)
        public const string HitFront = "Avatar_Female_Size02_Burnice_Ani_Hit_H_Front";
        public const string HitShake = "Avatar_Female_Size02_Burnice_Ani_Hit_Shake";

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            ApplyAnimatorSpeed();
        }

        public void ApplyAnimatorSpeed(float speed = 1f)
        {
            _animator.speed = speed;
        }

        // ── Play ──────────────────────────────────────────────────
        public void Play(string stateName, float crossFade = 0.1f)
        {
            _animator.CrossFade(stateName, crossFade, 0);
        }

        // 지정 레이어에 CrossFade (Additive 레이어 등 layer 0 외의 재생용)
        public void PlayAdditive(string stateName, int layer, float crossFade = 0.05f)
        {
            _animator.CrossFade(stateName, crossFade, layer);
        }

        // 레이어 가중치 제어 (Additive 적용 강도)
        public void SetLayerWeight(int layer, float weight)
        {
            _animator.SetLayerWeight(layer, weight);
        }

        // ── Hit Shake (Additive) ──────────────────────────────────
        // Hit config 클립의 Notify(EventName="OnHitShake")가 SendMessage로 호출한다.
        // additive 레이어에 Hit_Shake를 재생하고 끝나면 weight를 0으로 페이드.
        public void OnHitShake()
        {
            Debug.Log("[Hit] OnHitShake 호출 — layer1 additive 흔들림 재생", this);
            if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
            _shakeRoutine = StartCoroutine(ShakeRoutine());
        }

        private IEnumerator ShakeRoutine()
        {
            PlayAdditive(HitShake, _additiveLayer, 0.05f);
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

        // ── Locomotion Shortcuts ──────────────────────────────────
        public void PlayIdle()      => Play(Idle,      0.1f);
        public void PlayIdleLoop()  => Play(IdleLoop,  0.15f);
        public void PlayWalkStart() => Play(WalkStart, 0.1f);
        public void PlayWalkLoop()  => Play(WalkLoop,  0.1f);
        public void PlayWalkEnd()   => Play(WalkEnd,   0.1f);
        public void PlayRunLoop()   => Play(RunLoop,   0.1f);
        public void PlayRunEnd()    => Play(RunEnd,    0.1f);
        public void PlayTurnBack()  => Play(TurnBack,  0.1f);
        public void PlayTaunt()     => Play(Taunt,     0.15f);

        // ── Attack Shortcuts ──────────────────────────────────────
        public void PlayNormalAttack(int index)
        {
            string clip = index switch
            {
                1 => AttackNormal_01,
                2 => AttackNormal_02,
                3 => AttackNormal_03,
                4 => AttackNormal_04,
                _ => AttackNormal_05,
            };
            Play(clip);
        }

        public void PlayEnhanceAttack(int index)
        {
            string clip = index switch
            {
                1 => AttackEnhance_01,
                2 => AttackEnhance_02,
                _ => AttackEnhance_03,
            };
            Play(clip);
        }

        public void PlayRush()              => Play(AttackRush);
        public void PlaySpecial()           => Play(AttackSpecial);
        public void PlayExSpecial(int index) => Play(index == 1 ? AttackExSpecial_01 : AttackExSpecial_02);
        public void PlayCounter()           => Play(AttackCounter);
        public void PlayParryStart()        => Play(AttackParryStart);

        // ── Backward Compatibility ────────────────────────────────
        public float GetCurrentNormalizedTime(int layer = 0) => GetNormalizedTime(layer);

        // 현재 재생 중인 State 이름 반환 (알려진 상수 목록에서 매칭)
        public string GetCurrentStateName()
        {
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            foreach (string name in _allClipNames)
            {
                if (info.IsName(name))
                    return $"{System.IO.Path.GetFileName(name)}  ({info.normalizedTime:F2})";
            }
            return $"Unknown  ({info.normalizedTime:F2})";
        }

        public float GetCurrentNormalizedTimeRaw() =>
            _animator.GetCurrentAnimatorStateInfo(0).normalizedTime;

        private static readonly string[] _allClipNames =
        {
            Idle, IdleLoop, WalkStart, WalkLoop, WalkEnd,
            RunLoop, RunEnd, TurnBack, Taunt,
            AttackNormal_01, AttackNormal_02, AttackNormal_03, AttackNormal_04, AttackNormal_05,
            AttackEnhance_01, AttackEnhance_02, AttackEnhance_03,
            AttackRush, AttackSpecial, AttackExSpecial_01, AttackExSpecial_02,
            AttackCounter, AttackParryStart,
        };

        // ── State Query ───────────────────────────────────────────
        public bool IsCurrentState(string stateName, int layer = 0)
        {
            return _animator.GetCurrentAnimatorStateInfo(layer).IsName(stateName);
        }

        // normalizedTime >= 0.85 이면 클립 종료로 판단
        public bool IsStateDone(string stateName, int layer = 0)
        {
            var info = _animator.GetCurrentAnimatorStateInfo(layer);
            return info.IsName(stateName) && info.normalizedTime >= 0.85f;
        }

        public float GetNormalizedTime(int layer = 0)
        {
            return _animator.GetCurrentAnimatorStateInfo(layer).normalizedTime;
        }
    }
}
