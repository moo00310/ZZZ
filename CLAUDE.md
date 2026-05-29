# ZZZ Unity Project — CLAUDE.md

## 프로젝트 개요
URP 기반 전투 연출 데모. 플레이어(Burnice)가 허수아비를 공격하는 레벨 위에
애니메이션 툴, 이펙트 툴, 렌더 타겟 툴을 에디터 확장으로 제작.

## 폴더 구조
```
Assets/
├── 00.Model/               FBX, 텍스처 원본
├── 01.Characters/          캐릭터 Prefab, Animation, Material
├── 02.Effects/             VFX Graph / Particle System
├── 03.Shaders/             HLSL, ShaderLab, RenderFeature 셰이더
├── 04.Scripts/             런타임 C# (Player, Combat, UI, Utilities)
├── 05.Editor/              에디터 전용 C# (AnimationTool, EffectTool, RenderTargetTool)
├── 06.RenderPipeline/      URP RenderFeature, RendererData, Profiles
├── 07.Scenes/              게임 씬
└── 99.Settings/            URP 세팅 (변경 금지)
```

## 렌더 파이프라인
- **URP** 사용
- RenderFeature는 `06.RenderPipeline/RenderFeatures/` 에 위치
- 셰이더는 `03.Shaders/` 하위 목적별 폴더로 분리

## 코드 작성 규칙
자세한 내용은 [CodingConventions.md](Assets/Documentation/CodingConventions.md) 참고.

### 핵심 요약
- 네임스페이스: `ZZZ.<모듈>` (예: `ZZZ.Combat`, `ZZZ.Editor.AnimationTool`)
- 클래스명: `PascalCase`
- private 필드: `_camelCase` + `[SerializeField]` 로 인스펙터 노출
- 메서드명: `PascalCase`
- 에디터 스크립트는 반드시 `05.Editor/` 에 위치, `#if UNITY_EDITOR` 또는 `Editor` 어셈블리로 격리

## 작업 시 주의사항
- `99.Settings/` 내 URP 에셋은 직접 수정하지 않음 — 새 Profile/RendererData 생성해서 사용
- Editor 전용 코드에 런타임 의존성 추가 금지
- VFX / Particle은 Prefab으로 만들어 `02.Effects/` 에 저장
