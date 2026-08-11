using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ;
using ZZZ.Combat;
using ZZZ.Effects;
using ZZZ.Editor.Effects;

namespace ZZZ.Editor.AnimationTool
{
    // Effect 탭 — 애니 프리뷰와 같은 시간축으로 조합 이펙트를 캐릭터 소켓에 붙여 시뮬레이션하고,
    // 선택한 Effect Notify의 발동 시점(NormalizedTime)과 각 Entry(프리팹+배치/시차/풀링)를 프레임 보며 조정.
    // 순차(비-Combo) 모드 전용 — Combo 타이밍은 분기·동적이라 절대 시간 계산이 불가.
    public partial class AnimationConfigTool
    {
        // 스폰된 프리뷰 이펙트 하나(= 조합 Entry 하나).
        private class FxPreviewAtom
        {
            public GameObject Root;
            public List<ParticleSystem> Top;
            public int ClipIdx;
            public TrackNotify Notify;
            public CompositeEffectEntry Entry;

            // 분리/ParentToSpawnerRoot 프리뷰용 — 발동 순간의 소켓 포즈를 캡처한다.
            public Transform  Socket;
            public bool       Captured;
            public Vector3    CapPos;
            public Quaternion CapRot;

            // 파티클 노브 적용 대상(단일 PS)과 오버라이드 적용 전 프리팹 기본값(스폰 시 캡처)
            public ParticleSystem   OverrideTarget;
            public ParticleBaseline Baseline;
            // 머티리얼 스왑 대상(단일 렌더러)과 스왑 전 기본 머티리얼
            public Renderer OverrideRenderer;
            public Material BaseMaterial;
            public EffectProgressDriver[] ProgressDrivers;
            public List<EffectModule> ModulesSorted;
        }

        private readonly List<FxPreviewAtom> _fxAtoms = new List<FxPreviewAtom>();
        private MaterialPropertyBlock _fxMpb;   // 지연 생성 — 역직렬화 중 UnityObject 생성 금지라 필드 이니셜라이저 불가
        private bool _fxDirty = true;
        private int _fxPreviewClipIdx = -1;
        private int _fxPreviewNotifyIdx = -1;

        // 인스펙터 편집용
        private CompositeEffect  _fxComposite;
        private SerializedObject _fxCompositeSO;

        // 조합 내부 StartDelay 타임라인 드래그 상태
        private int   _fxTlDragEntry = -1;
        private float _fxTlGrabDx;

        // Entry별 접힘/풀링 폴드 상태(인덱스 기준)
        private readonly HashSet<int> _fxEntryCollapsed = new HashSet<int>();
        private readonly HashSet<int> _fxPoolFold       = new HashSet<int>();

        // 풀 개요
        private bool    _fxPoolOverviewFold;
        private Vector2 _fxPoolScroll;

        // ── 시뮬레이션 ────────────────────────────────────────────
        private void UpdateEffectPreview(float time)
        {
            if (_fxDirty) RebuildFxPreview();

            foreach (var a in _fxAtoms)
            {
                if (a.ClipIdx < 0 || a.ClipIdx >= _config.Clips.Count) continue;
                var tc = _config.Clips[a.ClipIdx];
                if (tc.Clip == null) continue;

                float clipDur = tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
                float fire    = GetClipStartTime(a.ClipIdx) + a.Notify.NormalizedTime * clipDur + a.Entry.StartDelay;
                float atomDur = Mathf.Max(EffectEditorShared.EntryDuration(a.Entry), 0.05f);
                float local   = time - fire;

                bool active = local >= 0f && local <= atomDur;
                if (a.Root.activeSelf != active) a.Root.SetActive(active);
                if (!active) { a.Captured = false; continue; }   // 비활성 → 다음 활성 진입 때 다시 캡처

                PlaceFxAtom(a);   // 오프셋/스케일 편집 실시간 반영 (ParentToSpawnerRoot는 캡처 포즈로 고정)
                EffectMaterialApplier.Apply(EffectModuleSettings.MaterialOverride(a.Entry), a.OverrideRenderer, a.BaseMaterial);   // 룩 통째 스왑
                if (_fxMpb == null) _fxMpb = new MaterialPropertyBlock();
                EffectParamApplier.Apply(a.Root, a.Entry, _fxMpb);   // 셰이더 노브 오버라이드 실시간 반영
                ParticleParamApplier.Apply(a.Entry, a.OverrideTarget, a.Baseline);   // 파티클 모듈 노브(Simulate 전)
                float speed = EffectModuleSettings.PlaybackSpeed(a.Entry);   // 시뮬 시간 압축으로 근사
                float playbackTime = Mathf.Min(local, atomDur) * speed;
                if (a.Entry.Modules != null && a.Entry.Modules.Count > 0)
                    SimulateFxModules(a, playbackTime);
                else
                    foreach (var ps in a.Top)
                        if (ps != null) ps.Simulate(playbackTime, true, true);
                foreach (var driver in a.ProgressDrivers)
                    if (driver != null) driver.Evaluate(playbackTime);
            }
            SceneView.RepaintAll();
        }

        private void SimulateFxModules(FxPreviewAtom atom, float playbackTime)
        {
            const float step = 1f / 60f;
            foreach (ParticleSystem ps in atom.Top)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float elapsed = 0f;
            while (elapsed < playbackTime)
            {
                EvaluateFxModules(atom, elapsed);
                float delta = Mathf.Min(step, playbackTime - elapsed);
                bool restart = elapsed <= 0f;
                foreach (ParticleSystem ps in atom.Top)
                    if (ps != null) ps.Simulate(delta, true, restart);
                elapsed += delta;
            }
            EvaluateFxModules(atom, playbackTime);
        }

        private void EvaluateFxModules(FxPreviewAtom atom, float localTime)
        {
            if (_target == null) return;
            for (int i = 0; i < atom.ModulesSorted.Count; i++)
                atom.ModulesSorted[i].EvaluatePreview(
                    atom.Root.transform, _target.transform, localTime);
        }

        private void RebuildFxPreview()
        {
            ClearFxPreviewInstances();
            _fxDirty = false;
            if (_target == null || _config == null) return;

            if (_notifyClipIdx < 0 || _notifyClipIdx >= _config.Clips.Count) return;

            TrackClip clip = _config.Clips[_notifyClipIdx];
            if (clip.Clip == null || _selectedNotify < 0 || _selectedNotify >= clip.Notifies.Count) return;

            TrackNotify notify = clip.Notifies[_selectedNotify];
            if (notify.Type == NotifyType.Effect && notify.Effect != null)
            {
                foreach (CompositeEffectEntry entry in notify.Effect.Entries)
                {
                    if (entry == null || entry.Prefab == null) continue;
                    SpawnFxAtom(_notifyClipIdx, notify, entry);
                }
                return;
            }

            if (notify.Payload is not HitNotifyPayload
                || notify.Hit.Origin != HitOrigin.Effect
                || string.IsNullOrEmpty(notify.Hit.EffectKey)) return;

            string bindingKey = notify.Hit.EffectKey;
            for (int clipIndex = 0; clipIndex < _config.Clips.Count; clipIndex++)
            {
                TrackClip effectClip = _config.Clips[clipIndex];
                for (int notifyIndex = 0;
                    notifyIndex < effectClip.Notifies.Count; notifyIndex++)
                {
                    TrackNotify effectNotify = effectClip.Notifies[notifyIndex];
                    if (effectNotify.Type != NotifyType.Effect
                        || effectNotify.Effect == null) continue;
                    foreach (CompositeEffectEntry entry in effectNotify.Effect.Entries)
                    {
                        if (entry == null || entry.Prefab == null
                            || !string.Equals(entry.BindingKey?.Trim(), bindingKey,
                                System.StringComparison.Ordinal)) continue;
                        SpawnFxAtom(clipIndex, effectNotify, entry);
                    }
                }
            }
        }

        private void SpawnFxAtom(int clipIdx, TrackNotify notify, CompositeEffectEntry entry)
        {
            Transform socket = FindSocket(entry.Socket);
            // 런타임과 같은 부모 규칙: FollowSpawner만 소켓, ParentToSpawnerRoot는 캐릭터 루트,
            // 둘 다 아니면 월드에 분리한다.
            bool toRoot = entry.ParentToSpawnerRoot && _target != null;
            Transform parent = toRoot
                ? _target.transform
                : entry.FollowSpawner ? socket : null;

            var go = Instantiate(entry.Prefab, parent);
            go.hideFlags = HideFlags.DontSave;

            var top = new List<ParticleSystem>();
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!EffectEditorShared.HasParticleAncestor(ps.transform, go.transform)) top.Add(ps);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            var target   = go.GetComponentInChildren<ParticleSystem>(true);
            var renderer = go.GetComponentInChildren<Renderer>(true);
            var modules = new List<EffectModule>();
            if (entry.Modules != null)
                for (int i = 0; i < entry.Modules.Count; i++)
                    if (entry.Modules[i] != null) modules.Add(entry.Modules[i]);
            modules.Sort((a, b) => a.EvaluationOrder.CompareTo(b.EvaluationOrder));
            var a = new FxPreviewAtom { Root = go, Top = top, ClipIdx = clipIdx, Notify = notify, Entry = entry, Socket = socket,
                                        OverrideTarget = target, Baseline = ParticleBaseline.Capture(target),
                                        OverrideRenderer = renderer, BaseMaterial = renderer != null ? renderer.sharedMaterial : null,
                                        ProgressDrivers = go.GetComponentsInChildren<EffectProgressDriver>(true),
                                        ModulesSorted = modules };
            if (entry.FollowSpawner && !toRoot) ApplyFxTransform(a);
            go.SetActive(false);
            _fxAtoms.Add(a);
        }

        private static void ApplyFxTransform(FxPreviewAtom a)
        {
            var t = a.Root.transform;
            t.localPosition    = a.Entry.PositionOffset;
            t.localEulerAngles = a.Entry.EulerOffset;
            t.localScale       = a.Entry.Scale;
        }

        // 런타임 PlaceInstance와 동일한 규칙으로 프리뷰 배치.
        // ParentToSpawnerRoot: 활성 진입(≈발동) 순간의 소켓 포즈(+오프셋)를 루트 로컬로 캡처해 고정 →
        // 이후 프레임에서 손(소켓)이 움직여도 따라가지 않고, 캐릭터 루트(_target)만 따라간다.
        private void PlaceFxAtom(FxPreviewAtom a)
        {
            if (a.Entry.ParentToSpawnerRoot && _target != null)
            {
                Transform root = _target.transform;
                if (!a.Captured)
                {
                    Transform socket = a.Socket != null ? a.Socket : root;
                    // 회전 기준 프레임 — 런타임 PlaceInstance와 동일. IgnoreSocketRotation이면 캐릭터 루트(_target) 기준.
                    Quaternion rootFrame = a.Entry.IgnoreSocketRotation ? root.rotation : socket.rotation;
                    Vector3    wpos = a.Entry.IgnoreSocketRotation
                        ? socket.position + rootFrame * a.Entry.PositionOffset
                        : socket.TransformPoint(a.Entry.PositionOffset);
                    Quaternion wrot = rootFrame * Quaternion.Euler(a.Entry.EulerOffset);
                    a.CapPos   = root.InverseTransformPoint(wpos);
                    a.CapRot   = Quaternion.Inverse(root.rotation) * wrot;
                    a.Captured = true;
                }
                var t = a.Root.transform;
                t.localPosition = a.CapPos;
                t.localRotation = a.CapRot;
                t.localScale    = a.Entry.Scale;
                return;
            }

            if (a.Entry.FollowSpawner)
            {
                ApplyFxTransform(a);
                return;
            }

            // FollowSpawner가 꺼진 런타임 이펙트는 발동 순간의 월드 포즈에 분리되어
            // 이후 소켓 본의 이동/회전을 따라가지 않는다.
            if (a.Captured) return;
            Transform anchor = a.Socket != null ? a.Socket : _target.transform;
            Quaternion detachedFrame = a.Entry.IgnoreSocketRotation ? _target.transform.rotation : anchor.rotation;
            Transform detachedTransform = a.Root.transform;
            detachedTransform.position = a.Entry.IgnoreSocketRotation
                ? anchor.position + detachedFrame * a.Entry.PositionOffset
                : anchor.TransformPoint(a.Entry.PositionOffset);
            detachedTransform.rotation = detachedFrame * Quaternion.Euler(a.Entry.EulerOffset);
            detachedTransform.localScale = a.Entry.Scale;
            a.Captured = true;
        }

        // Entry 접힘 상태는 조합별로 SessionState에 저장 — 스크립트 재컴파일(도메인 리로드)에도 유지된다.
        // (일반 필드는 리로드마다 초기화되어 접었던 게 다시 펼쳐지는 게 불편했음)
        private string FxCollapseKey =>
            _fxComposite != null ? "ACT.fxCollapse." + _fxComposite.GetInstanceID() : null;

        private void LoadEntryCollapse()
        {
            _fxEntryCollapsed.Clear();
            string key = FxCollapseKey;
            if (key == null) return;
            string s = SessionState.GetString(key, "");
            if (string.IsNullOrEmpty(s)) return;
            foreach (var tok in s.Split(','))
                if (int.TryParse(tok, out int idx)) _fxEntryCollapsed.Add(idx);
        }

        private void SaveEntryCollapse()
        {
            string key = FxCollapseKey;
            if (key != null) SessionState.SetString(key, string.Join(",", _fxEntryCollapsed));
        }

        private Transform FindSocket(string socketName)
        {
            if (_target == null) return null;
            if (string.IsNullOrEmpty(socketName)) return _target.transform;
            var found = FindDescendant(_target.transform, socketName);
            return found != null ? found : _target.transform;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDescendant(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private void ClearFxPreviewInstances()
        {
            foreach (var a in _fxAtoms)
                if (a.Root != null) DestroyImmediate(a.Root);
            _fxAtoms.Clear();
        }

        private void ClearFxPreview()
        {
            ClearFxPreviewInstances();
            _fxDirty = true;
        }

        // ── Effect 편집 섹션 (Notify 인스펙터 안에 인라인) ─────────
        // Notify 타입이 Effect일 때 DrawNotifyInspector에서 호출된다. 발동 시점/구간끝은
        // 상위 인스펙터(프레임 필드)에서 이미 편집하므로 여기선 '조합(Composite)' 편집만 담당.
        private void DrawEffectSection(TrackClip clip, TrackNotify notify)
        {
            // 조합 참조 + 새 조합 생성
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var comp = (CompositeEffect)EditorGUILayout.ObjectField(
                "Effect (Composite)", notify.Effect, typeof(CompositeEffect), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Assign Composite");
                notify.Effect = comp;
                EditorUtility.SetDirty(_config);
                _fxDirty = true;
            }
            if (GUILayout.Button("New", GUILayout.Width(40)))
            {
                var c = EffectEditorShared.CreateAsset<CompositeEffect>("Cmp_New");
                Undo.RecordObject(_config, "Assign Composite");
                notify.Effect = c;
                EditorUtility.SetDirty(_config);
                _fxDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            if (notify.Effect == null)
            {
                EditorGUILayout.HelpBox("CompositeEffect를 지정하거나 New로 생성하세요.", MessageType.Info);
                return;
            }

            if (_comboMode)
                EditorGUILayout.HelpBox("Combo 모드에선 씬 이펙트 프리뷰가 꺼집니다. 순차 모드로 보세요.",
                    MessageType.None);
            else if (_target == null)
                EditorGUILayout.HelpBox("상단에서 Target(캐릭터)을 지정하면 씬에서 미리보기됩니다.",
                    MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("조합 내부 시차 (막대 드래그 = StartDelay · 우측 엣지 = Duration)", EditorStyles.miniBoldLabel);
            float tlH = 22f + notify.Effect.Entries.Count * 20f;
            Rect tl = GUILayoutUtility.GetRect(10f, tlH, GUILayout.ExpandWidth(true));

            float clipDur2 = clip.Clip != null ? clip.Clip.length / Mathf.Max(0.01f, clip.Speed) : 0f;
            float fireBase = GetClipStartTime(_notifyClipIdx) + notify.NormalizedTime * clipDur2;
            float local    = _trackTime - fireBase;
            float ph = (local >= 0f && local <= EffectEditorShared.CompositeDuration(notify.Effect)) ? local : -1f;
            EffectEditorShared.DrawStartDelayTimeline(tl, notify.Effect, ref _fxTlDragEntry, ref _fxTlGrabDx, ph, null);

            EditorGUILayout.Space(4f);
            DrawCompositeEntries(notify.Effect);

            // 풀 개요 (플레이 모드)
            EditorGUILayout.Space(6f);
            _fxPoolOverviewFold = EditorGUILayout.Foldout(_fxPoolOverviewFold, "Pool Overview (Play 모드)", true);
            if (_fxPoolOverviewFold)
            {
                if (EditorApplication.isPlaying) EffectEditorShared.DrawPoolTable(ref _fxPoolScroll);
                else EditorGUILayout.HelpBox("플레이 모드에서 프리팹별 Free/Live/Created 수치가 표시됩니다.", MessageType.None);
            }

            // 편집 결과를 현재 플레이헤드에서 즉시 반영
            if (Event.current.type == EventType.Repaint && !EditorApplication.isPlaying
                && !_comboMode && AnimationMode.InAnimationMode())
                UpdateEffectPreview(_trackTime);
        }

        // 조합 Entry들 — 프리팹 + 스폰 위치(소켓/오프셋/스케일) + 조합 내 시차 + 풀링/반납.
        // 프리팹/소켓 변경은 구조 변경이라 재생성(_fxDirty), 그 외는 실시간 반영.
        private void DrawCompositeEntries(CompositeEffect composite)
        {
            if (_fxComposite != composite || _fxCompositeSO == null)
            {
                _fxComposite   = composite;
                _fxCompositeSO = new SerializedObject(composite);
                LoadEntryCollapse();   // 조합별 접힘 상태 복원 (도메인 리로드에도 유지)
            }
            _fxCompositeSO.Update();

            var entries = _fxCompositeSO.FindProperty("Entries");
            EditorGUILayout.LabelField("Entries (프리팹별 스폰 설정)", EditorStyles.boldLabel);

            for (int i = 0; i < entries.arraySize; i++)
            {
                var e = entries.GetArrayElementAtIndex(i);
                var prefabProp = e.FindPropertyRelative("Prefab");
                var prefab = prefabProp.objectReferenceValue as GameObject;
                string title = prefab != null ? prefab.name : "(프리팹 미지정)";
                float delay = e.FindPropertyRelative("StartDelay").floatValue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                bool expanded = !_fxEntryCollapsed.Contains(i);
                bool newExp = EditorGUILayout.Foldout(expanded, $"#{i}  {title}   @{delay:0.##}s", true);
                if (newExp != expanded)
                {
                    if (newExp) _fxEntryCollapsed.Remove(i); else _fxEntryCollapsed.Add(i);
                    SaveEntryCollapse();   // 접힘 상태 세션 저장 (재컴파일에도 유지)
                }
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.8f, 0.35f, 0.35f);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    entries.DeleteArrayElementAtIndex(i);
                    _fxCompositeSO.ApplyModifiedProperties();
                    _fxDirty = true;
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                if (newExp)
                {
                    // 필드 표시 순서는 두 툴 공용(EffectEditorShared). 구조 필드(Prefab/Socket) 변경 시 재생성.
                    bool structural = EffectEditorShared.DrawEntryFields(e, prefabProp, prefab);

                    // 풀링/반납 — 접기
                    bool poolFold = _fxPoolFold.Contains(i);
                    bool newPool = EditorGUILayout.Foldout(poolFold, "반납 설정", true);
                    if (newPool != poolFold) { if (newPool) _fxPoolFold.Add(i); else _fxPoolFold.Remove(i); }
                    if (newPool)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUI.BeginChangeCheck();
                        EffectEditorShared.DrawEntryPoolFields(e, prefab);
                        if (EditorGUI.EndChangeCheck()) _fxDirty = true;
                        EditorGUI.indentLevel--;
                    }

                    if (structural) { _fxCompositeSO.ApplyModifiedProperties(); _fxDirty = true; }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2f);
            }

            if (GUILayout.Button("+ Add Entry"))
            {
                int n = entries.arraySize;
                entries.InsertArrayElementAtIndex(n);
                var e = entries.GetArrayElementAtIndex(n);
                e.FindPropertyRelative("Prefab").objectReferenceValue = null;
                e.FindPropertyRelative("BindingKey").stringValue = "";
                e.FindPropertyRelative("StartDelay").floatValue = 0f;
                e.FindPropertyRelative("Duration").floatValue = 0f;
                e.FindPropertyRelative("PlaybackSpeed").floatValue = 1f;
                e.FindPropertyRelative("Socket").stringValue = "";
                e.FindPropertyRelative("PositionOffset").vector3Value = Vector3.zero;
                e.FindPropertyRelative("EulerOffset").vector3Value = Vector3.zero;
                e.FindPropertyRelative("Scale").vector3Value = Vector3.one;
                e.FindPropertyRelative("FollowSpawner").boolValue = false;
                e.FindPropertyRelative("ParentToSpawnerRoot").boolValue = false;
                e.FindPropertyRelative("IgnoreSocketRotation").boolValue = false;
                e.FindPropertyRelative("Despawn").enumValueIndex = (int)DespawnMode.ParticleStopped;
                e.FindPropertyRelative("Lifetime").floatValue = 0f;
                e.FindPropertyRelative("ParamOverrides").ClearArray();
                var modules = e.FindPropertyRelative("Modules");
                modules.ClearArray();
                modules.InsertArrayElementAtIndex(0);
                modules.GetArrayElementAtIndex(0).managedReferenceValue =
                    new ParticlePlaybackEffectModule();
                _fxCompositeSO.ApplyModifiedProperties();
                _fxDirty = true;
            }

            _fxCompositeSO.ApplyModifiedProperties();
        }
    }
}
