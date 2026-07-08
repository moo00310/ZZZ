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
}
