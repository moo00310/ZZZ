using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ.Combat;
using ZZZ.Effects;
using ZZZ.Editor.Effects;

namespace ZZZ.Editor.EffectTool
{
    public partial class EffectTool
    {
        // 씬 원점에 조합/원자를 스폰해 ParticleSystem.Simulate로 스크럽 프리뷰한다(에디트 모드 전용).
        private class PreviewInstance
        {
            public GameObject Root;
            public List<ParticleSystem> TopSystems;   // 최상위 파티클(자식은 withChildren로 딸려감)
            public CompositeEffectEntry Entry;        // 시차/재생 길이/속도 등 프리뷰에 반영할 설정
            public ParticleSystem OverrideTarget;     // 파티클 노브 적용 대상(단일 PS)
            public ParticleBaseline Baseline;         // 오버라이드 끈 필드 복원용 프리팹 기본값(스폰 시 캡처)
            public Renderer OverrideRenderer;         // 머티리얼 스왑 대상(단일 렌더러)
            public Material BaseMaterial;             // 스왑 전 프리팹 기본 머티리얼
            public EffectProgressDriver[] ProgressDrivers;
        }

        private GameObject _previewRoot;
        private readonly List<PreviewInstance> _previewInstances = new List<PreviewInstance>();
        private MaterialPropertyBlock _previewMpb;   // 지연 생성 — 역직렬화 중 UnityObject 생성 금지
        private float  _previewTime;
        private bool   _previewPlaying;
        private double _previewLastTime;

        private void DrawPreviewBar(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.20f, 0.20f, 0.20f));
            GUILayout.BeginArea(new Rect(area.x + 6f, area.y + 4f, area.width - 12f, area.height - 6f));
            GUILayout.BeginHorizontal();

            bool hasTarget = _selectedComposite != null;
            using (new EditorGUI.DisabledScope(!hasTarget || EditorApplication.isPlaying))
            {
                if (GUILayout.Button(_previewPlaying ? "⏸ Pause" : "▶ Play", GUILayout.Width(80)))
                    TogglePreviewPlay();
                if (GUILayout.Button("■ Stop", GUILayout.Width(64)))
                    StopPreview();

                float dur = PreviewDuration();
                float t   = EditorGUILayout.Slider(_previewTime, 0f, dur);
                if (!Mathf.Approximately(t, _previewTime))
                {
                    _previewTime = t;
                    ScrubPreview(t);
                }
                GUILayout.Label($"/ {dur:0.00}s", GUILayout.Width(60));
            }

            if (EditorApplication.isPlaying)
                GUILayout.Label("(플레이 모드 — 프리뷰 비활성)", EditorStyles.miniLabel);

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private float PreviewDuration()
        {
            if (_selectedComposite != null) return EffectEditorShared.CompositeDuration(_selectedComposite);
            return 1f;
        }

        private void TogglePreviewPlay()
        {
            if (_previewPlaying) { _previewPlaying = false; return; }
            EnsurePreviewSpawned();
            if (_previewRoot == null) return;
            if (_previewTime >= PreviewDuration()) _previewTime = 0f;
            _previewPlaying  = true;
            _previewLastTime = EditorApplication.timeSinceStartup;
        }

        private void TickPreview()
        {
            if (!_previewPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            _previewTime += (float)(now - _previewLastTime);
            _previewLastTime = now;

            float dur = PreviewDuration();
            if (_previewTime >= dur) _previewTime = 0f;   // 루프 프리뷰
            ScrubPreview(_previewTime);
        }

        // 스폰 인스턴스들을 t(글로벌 시간)에 맞춰 시뮬레이션. t < StartDelay면 아직 안 뜬 것으로 숨김.
        private void ScrubPreview(float t)
        {
            if (_previewRoot == null) return;
            foreach (var inst in _previewInstances)
            {
                float local = t - inst.Entry.StartDelay;
                bool active = local >= 0f;
                if (inst.Root.activeSelf != active) inst.Root.SetActive(active);
                if (!active) continue;

                // 머티리얼 스왑(룩 통째) → 셰이더 노브 MPB는 그 위에 얹힘
                EffectMaterialApplier.Apply(inst.Entry.MaterialOverride, inst.OverrideRenderer, inst.BaseMaterial);
                // 셰이더 노브 오버라이드 실시간 반영(런타임 Bind와 동일 로직)
                if (_previewMpb == null) _previewMpb = new MaterialPropertyBlock();
                EffectParamApplier.Apply(inst.Root, inst.Entry, _previewMpb);
                // 파티클 모듈 노브(수명/Size커브/색) — Simulate 전에 적용해야 시뮬에 반영됨
                ParticleParamApplier.Apply(inst.Entry, inst.OverrideTarget, inst.Baseline);

                // PlaybackSpeed는 시뮬 시간 압축으로, Duration은 유효 길이 클램프로 근사
                float speed = inst.Entry.PlaybackSpeed > 0f ? inst.Entry.PlaybackSpeed : 1f;
                float dur   = Mathf.Max(EffectEditorShared.EntryDuration(inst.Entry), 0.05f);
                float playbackTime = Mathf.Min(local, dur) * speed;
                foreach (var ps in inst.TopSystems)
                {
                    if (ps == null) continue;
                    ps.Simulate(playbackTime, true, true);
                }
                foreach (var driver in inst.ProgressDrivers)
                    if (driver != null) driver.Evaluate(playbackTime);
            }
            SceneView.RepaintAll();
        }

        private void EnsurePreviewSpawned()
        {
            if (_previewRoot != null) return;

            _previewRoot = new GameObject("~EffectPreview") { hideFlags = HideFlags.DontSave };
            _previewInstances.Clear();

            if (_selectedComposite != null)
                foreach (var e in _selectedComposite.Entries)
                {
                    if (e == null || e.Prefab == null) continue;
                    SpawnPreviewInstance(e);
                }

            if (_previewInstances.Count == 0) { StopPreview(); return; }
            ScrubPreview(_previewTime);
        }

        private void SpawnPreviewInstance(CompositeEffectEntry entry)
        {
            var go = Instantiate(entry.Prefab, _previewRoot.transform);
            if (go == null) return;
            go.hideFlags = HideFlags.DontSave;

            var t = go.transform;
            t.localPosition    = entry.PositionOffset;
            t.localEulerAngles = entry.EulerOffset;
            t.localScale       = entry.Scale;

            // 최상위 파티클만 수집(자식은 Simulate(withChildren)로 딸려감)
            var top = new List<ParticleSystem>();
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!EffectEditorShared.HasParticleAncestor(ps.transform, t)) top.Add(ps);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            // 파티클 노브 대상(단일 PS) + 머티리얼 스왑 대상(단일 렌더러) + 오버라이드 적용 전 기본값 캡처
            var target   = go.GetComponentInChildren<ParticleSystem>(true);
            var renderer = go.GetComponentInChildren<Renderer>(true);
            _previewInstances.Add(new PreviewInstance
            {
                Root = go, TopSystems = top, Entry = entry,
                OverrideTarget = target, Baseline = ParticleBaseline.Capture(target),
                OverrideRenderer = renderer, BaseMaterial = renderer != null ? renderer.sharedMaterial : null,
                ProgressDrivers = go.GetComponentsInChildren<EffectProgressDriver>(true),
            });
        }

        private void StopPreview()
        {
            _previewPlaying = false;
            _previewInstances.Clear();
            if (_previewRoot != null)
            {
                DestroyImmediate(_previewRoot);
                _previewRoot = null;
            }
        }
    }
}
