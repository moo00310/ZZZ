using UnityEngine;

namespace ZZZ.Effects
{
    // 프리팹이 EffectParameterSet으로 선언한 셰이더 노브를 Entry의 오버라이드(없으면 프리팹 기본값)로
    // 인스턴스 렌더러들에 MPB 적용한다. 런타임(PooledEffectHandle.Bind)과 에디터 프리뷰가 공유한다 —
    // 같은 로직을 써야 프리뷰와 인게임 룩이 일치한다.
    //
    // 선언된 파라미터를 "매번 전부 명시"한다: 오버라이드 없는 것도 기본값으로 써줘야 풀 재사용 시
    // 이전 조합 값이 안 남는다. GetPropertyBlock 기반이라 EffectProgressDriver의 _Progress 같은
    // 다른 MPB 값과 같은 렌더러에서 공존한다(우리 선언 키만 덮는다).
    public static class EffectParamApplier
    {
        // 캐시된 참조로 적용(런타임 핫패스). renderers/mpb는 호출자가 인스턴스 단위로 재사용.
        // allowRandomize: Randomize 노브를 이번 호출에서 굴릴지. 런타임(Bind)은 재생당 1회라 true,
        // 에디터 프리뷰는 매 프레임 호출이라 false(굴리면 프레임마다 튀어 보임 — 프리뷰는 기본값 표시).
        // Randomize 값은 렌더러 루프 전에 1회만 굴려, 한 인스턴스의 모든 렌더러가 같은 값을 받게 한다.
        public static void Apply(CompositeEffectEntry entry, EffectParameterSet set,
            Renderer[] renderers, MaterialPropertyBlock mpb, bool allowRandomize = true)
        {
            if (entry == null || set == null || renderers == null) return;
            int n = set.Parameters.Count;
            if (n == 0) return;
            if (_floatBuf.Length < n) { _floatBuf = new float[n]; _colorBuf = new Color[n]; }

            // 1) 값 선해석(랜덤은 여기서 1회 굴림)
            for (int i = 0; i < n; i++)
            {
                var decl = set.Parameters[i];
                if (decl == null || string.IsNullOrEmpty(decl.ShaderProperty)) continue;
                var ov = FindOverride(entry, decl.ShaderProperty);
                if (decl.Type == EffectParamType.Color)
                {
                    _colorBuf[i] = ov != null ? ov.ColorValue : decl.DefaultColor;
                }
                else if (ov != null && ov.Randomize && allowRandomize && decl.Type == EffectParamType.Float)
                {
                    _floatBuf[i] = decl.HasRange ? Random.Range(decl.Min, decl.Max) : Random.Range(0f, 100f);
                }
                else
                {
                    _floatBuf[i] = ov != null ? ov.FloatValue : decl.DefaultFloat;
                }
            }

            // 2) 렌더러마다 선해석 값 적용
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);   // 현재 블록 보존(_Progress 등) 후 우리 키만 덮는다
                for (int i = 0; i < n; i++)
                {
                    var decl = set.Parameters[i];
                    if (decl == null || string.IsNullOrEmpty(decl.ShaderProperty)) continue;
                    if (decl.Type == EffectParamType.Color) mpb.SetColor(decl.ShaderProperty, _colorBuf[i]);
                    else                                    mpb.SetFloat(decl.ShaderProperty, _floatBuf[i]);
                }
                r.SetPropertyBlock(mpb);
            }
        }

        // 인스턴스에서 즉석 해석 후 적용(에디터 프리뷰용). mpb는 재사용 가능(null이면 임시 생성).
        public static void Apply(GameObject instance, CompositeEffectEntry entry,
            MaterialPropertyBlock mpb = null, bool allowRandomize = false)
        {
            if (instance == null || entry == null) return;
            var set = instance.GetComponent<EffectParameterSet>();
            if (set == null || set.Parameters.Count == 0) return;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            Apply(entry, set, renderers, mpb ?? new MaterialPropertyBlock(), allowRandomize);
        }

        // 선해석용 재사용 버퍼(단일 스레드 메인 스레드 전용). 파라미터 수만큼 확장.
        private static float[] _floatBuf = new float[8];
        private static Color[] _colorBuf = new Color[8];

        public static EffectParamOverride FindOverride(CompositeEffectEntry entry, string shaderProperty)
        {
            var list = entry.ParamOverrides;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].ShaderProperty == shaderProperty) return list[i];
            return null;
        }
    }

    // 룩 통째 교체 노브 — 단일 렌더러의 sharedMaterial을 조합별로 스왑(런타임 Bind + 에디터 프리뷰 공용).
    // null 오버라이드는 baseline(프리팹 기본 머티리얼)으로 되돌려 풀 재사용 누수를 막는다.
    // sharedMaterial 참조 대입이라 인스턴스화/릭/alloc 없음. 셰이더 노브(MPB)는 이 위에 얹혀 공존한다.
    public static class EffectMaterialApplier
    {
        public static void Apply(Material ov, Renderer r, Material baseline)
        {
            if (r == null) return;
            r.sharedMaterial = ov != null ? ov : baseline;
        }
    }

    // 프리팹 파티클(단일 PS)의 기본 모듈값 스냅샷. 풀 재사용 시 오버라이드 안 한 필드를
    // 이 값으로 되돌리려면 원본이 필요하다(= 셰이더 노브의 EffectParamDecl.Default* 역할).
    // 파티클은 기본값이 데이터가 아니라 컴포넌트에 구워져 있어, 최초 1회 라이브로 캡처해 캐시한다.
    public struct ParticleBaseline
    {
        public bool                          Valid;
        public ParticleSystem.MinMaxCurve    StartLifetime;
        public ParticleSystem.MinMaxGradient StartColor;
        public bool                          SizeEnabled;
        public ParticleSystem.MinMaxCurve    Size;

        public static ParticleBaseline Capture(ParticleSystem ps)
        {
            var b = new ParticleBaseline();
            if (ps == null) return b;
            var main = ps.main;
            var sol  = ps.sizeOverLifetime;
            b.Valid         = true;
            b.StartLifetime = main.startLifetime;
            b.StartColor    = main.startColor;
            b.SizeEnabled   = sol.enabled;
            b.Size          = sol.size;
            return b;
        }
    }

    // 파티클 모듈 노브를 단일 PS에 적용. 셰이더 MPB(EffectParamApplier)와 대칭 — 런타임 Bind와
    // 에디터 프리뷰가 공유해 룩을 일치시킨다. StartLifetime은 Entry 일반 필드('0=프리팹 기본값'),
    // Size커브/시작색은 토글 오버라이드. 미적용(0/토글 off)은 baseline으로 되돌려 풀 재사용 누수를 막는다.
    public static class ParticleParamApplier
    {
        public static void Apply(CompositeEffectEntry entry, ParticleSystem ps, in ParticleBaseline baseline)
        {
            if (ps == null || entry == null || !baseline.Valid) return;

            ParticleAppearanceEffectModule appearance = EffectModuleSettings.Appearance(entry);
            var main = ps.main;
            float startLifetime = EffectModuleSettings.StartLifetime(entry);
            main.startLifetime = startLifetime > 0f
                ? (ParticleSystem.MinMaxCurve)startLifetime : baseline.StartLifetime;

            var ov = entry.ParticleOverride;
            bool overrideColor = appearance != null
                ? appearance.OverrideStartColor
                : ov != null && ov.OverrideStartColor;
            Color startColor = appearance != null
                ? appearance.StartColor
                : ov != null ? ov.StartColor : Color.white;
            main.startColor = overrideColor
                ? (ParticleSystem.MinMaxGradient)startColor : baseline.StartColor;

            var sol = ps.sizeOverLifetime;
            bool overrideSize = appearance != null
                ? appearance.OverrideSizeCurve
                : ov != null && ov.OverrideSizeCurve;
            if (overrideSize)
            {
                sol.enabled = true;
                float multiplier = appearance != null ? appearance.SizeMultiplier : ov.SizeMultiplier;
                AnimationCurve curve = appearance != null ? appearance.SizeCurve : ov.SizeCurve;
                sol.size = new ParticleSystem.MinMaxCurve(multiplier, curve);
            }
            else
            {
                sol.enabled = baseline.SizeEnabled;
                sol.size    = baseline.Size;
            }
        }
    }
}
