using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Effects
{
    public enum EffectParamType
    {
        Bool,    // 0/1 (셰이더 Boolean 프로퍼티 + Branch). 키워드가 아니라 값이어야 MPB로 먹는다.
        Float,
        Color,
    }

    // 이펙트 프리팹이 "조합 툴에서 조정 가능하게 노출할 셰이더 노브"를 스스로 선언하는 목록.
    // 프리팹 루트에 붙인다. 순수 에디터용 메타데이터(무엇을·어떻게 그릴지) — 런타임 적용은
    // Entry의 EffectParamOverride 값 + PooledEffectHandle의 MPB가 담당한다.
    // raw 셰이더 프로퍼티를 통째 노출하지 않고, 여기 등록한 것만 툴에 뜬다(큐레이션).
    public class EffectParameterSet : MonoBehaviour
    {
        public List<EffectParamDecl> Parameters = new List<EffectParamDecl>();

        public EffectParamDecl Find(string shaderProperty)
        {
            for (int i = 0; i < Parameters.Count; i++)
                if (Parameters[i] != null && Parameters[i].ShaderProperty == shaderProperty)
                    return Parameters[i];
            return null;
        }
    }

    [Serializable]
    public class EffectParamDecl
    {
        public string          DisplayName    = "";          // 툴 라벨 (예: "Seed")
        public string          ShaderProperty = "";          // MPB 키 (예: "_Seed")
        public EffectParamType Type           = EffectParamType.Float;

        // 미오버라이드 시 사용할 기본값 = 머티리얼에 구운 값과 맞춰둘 것.
        // Bind가 오버라이드 없는 선언 파라미터를 이 값으로 명시해 풀 재사용 누수를 막는다.
        public float DefaultFloat = 0f;                       // Bool은 0/1, Float은 값
        public Color DefaultColor = Color.white;

        // Float 슬라이더 범위(선택). HasRange=false면 자유 입력.
        public bool  HasRange = false;
        public float Min      = 0f;
        public float Max      = 1f;
    }

    // 조합 Entry가 저장하는 실제 오버라이드 값(이름-값). MPB가 아니라 직렬화되는 데이터다.
    [Serializable]
    public class EffectParamOverride
    {
        public string          ShaderProperty = "";
        public EffectParamType Type           = EffectParamType.Float;
        public float           FloatValue     = 0f;           // Bool은 0/1, Float은 값
        public Color           ColorValue     = Color.white;

        // Float 전용 — 켜면 FloatValue 대신 재생마다 범위 내 랜덤값을 씀(런타임 Bind에서 굴림).
        // 범위는 프리팹 선언(EffectParamDecl.HasRange/Min/Max), 미설정 시 0~100.
        public bool            Randomize      = false;
    }
}
