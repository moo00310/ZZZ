using UnityEngine;

namespace ZZZ.Combat
{
    // 이펙트 프리팹에 붙여, 파티클 재생 시간에 맞춰 셰이더의 _Progress를 0→1로 흘려준다.
    // FX_FlameBurst 등 _Progress 프로퍼티로 연출(디졸브/회색 전환 등)을 구동하는 머티리얼용.
    //
    // 흐름: Effect Notify가 이 프리팹을 Instantiate(ConfigState.DispatchNotify) →
    // 매 프레임 ParticleSystem.time을 읽어 진행도 계산.
    // 값은 MaterialPropertyBlock으로 렌더러 단위 격리 → 같은 .mat을 공유해도 인스턴스끼리 안 섞인다.
    //
    // [ExecuteAlways]: 파티클 패널 Play(씬 프리뷰)에서도 보이게 한다.
    // MonoBehaviour.Update는 원래 플레이 모드에서만 돌기 때문에, 이게 없으면 프리뷰 땐
    // _Progress가 머티리얼 기본값에 멈춰 "셰이더가 안 먹는 것처럼" 보인다.
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystemRenderer))]
    public class EffectProgressDriver : MonoBehaviour
    {
        // 기본은 파티클 시스템 Duration(이펙트 전체 길이)과 동기화. 끄면 _duration을 직접 쓴다.
        [SerializeField] private bool  _syncToDuration = true;
        [SerializeField] private float _duration       = 1f;    // _syncToDuration=false일 때만 사용(초)
        [SerializeField] private bool  _clamp          = true;  // 끝난 뒤 1로 고정(false면 계속 증가)

        private static readonly int s_progressId = Shader.PropertyToID("_Progress");

        private ParticleSystem         _ps;
        private ParticleSystemRenderer _renderer;
        private MaterialPropertyBlock  _mpb;

        private void Awake() => EnsureInit();

        // ExecuteAlways라 도메인 리로드 후 Awake 없이 Update가 도는 경우가 있어,
        // 참조가 null이면 그때 다시 잡는다.
        private void EnsureInit()
        {
            if (_ps       == null) _ps       = GetComponent<ParticleSystem>();
            if (_renderer == null) _renderer = GetComponent<ParticleSystemRenderer>();
            if (_mpb      == null) _mpb      = new MaterialPropertyBlock();
        }

        // 이펙트 전체 길이(Duration). 이걸로 재생시간을 나눠 0~1 정규화한다.
        private float ResolveSpan()
        {
            return _syncToDuration ? _ps.main.duration : _duration;
        }

        private void Update()
        {
            EnsureInit();

            // Playback Time(_ps.time)을 전체 길이로 나눈 정규화 진행도.
            // 플레이 모드/씬 프리뷰 모두 파티클과 자동 동기화.
            float span = ResolveSpan();
            float p    = span > 0f ? _ps.time / span : 1f;
            if (_clamp) p = Mathf.Clamp01(p);

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(s_progressId, p);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
