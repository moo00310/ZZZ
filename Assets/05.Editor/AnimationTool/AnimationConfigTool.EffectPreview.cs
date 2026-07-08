using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ;
using ZZZ.Effects;
using ZZZ.Editor.Effects;

namespace ZZZ.Editor.AnimationTool
{
    // Effect 탭 — 애니 프리뷰와 같은 시간축으로 조합 이펙트를 캐릭터 소켓에 붙여 시뮬레이션하고,
    // 선택한 Effect Notify의 발동 시점(NormalizedTime)과 각 Entry(프리팹+배치/시차/풀링)를 프레임 보며 조정.
    // 순차(비-Combo) 모드 전용 — Combo 타이밍은 분기·동적이라 절대 시간 계산이 불가.
    public partial class AnimationConfigTool
    {
        // 스폰된 프리뷰 이펙트 하나(= 조합 Entry 하나). 소켓 본에 붙어 샘플된 포즈를 따라간다.
        private class FxPreviewAtom
        {
            public GameObject Root;
            public List<ParticleSystem> Top;
            public int ClipIdx;
            public TrackNotify Notify;
            public CompositeEffectEntry Entry;

            // ParentToSpawnerRoot 프리뷰용 — 발동(활성 진입) 순간 소켓 포즈를 루트 로컬로 캡처해 고정.
            // 손 스윙(이후 프레임의 소켓 이동)을 따라가지 않고 캐릭터 루트만 따라가게 한다(런타임과 동일).
            public Transform  Socket;
            public bool       Captured;
            public Vector3    CapPos;
            public Quaternion CapRot;
        }

        private readonly List<FxPreviewAtom> _fxAtoms = new List<FxPreviewAtom>();
        private MaterialPropertyBlock _fxMpb;   // 지연 생성 — 역직렬화 중 UnityObject 생성 금지라 필드 이니셜라이저 불가
        private bool _fxDirty = true;

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
                if (_fxMpb == null) _fxMpb = new MaterialPropertyBlock();
                EffectParamApplier.Apply(a.Root, a.Entry, _fxMpb);   // 셰이더 노브 오버라이드 실시간 반영
                float speed = a.Entry.PlaybackSpeed > 0f ? a.Entry.PlaybackSpeed : 1f;   // 시뮬 시간 압축으로 근사
                foreach (var ps in a.Top)
                    if (ps != null) ps.Simulate(Mathf.Min(local, atomDur) * speed, true, true);
            }
            SceneView.RepaintAll();
        }

        private void RebuildFxPreview()
        {
            ClearFxPreviewInstances();
            _fxDirty = false;
            if (_target == null || _config == null) return;

            for (int i = 0; i < _config.Clips.Count; i++)
            {
                var tc = _config.Clips[i];
                if (tc.Clip == null) continue;

                foreach (var notify in tc.Notifies)
                {
                    if (notify.Type != NotifyType.Effect || notify.Effect == null) continue;
                    foreach (var entry in notify.Effect.Entries)
                    {
                        if (entry == null || entry.Prefab == null) continue;
                        SpawnFxAtom(i, notify, entry);
                    }
                }
            }
        }

        private void SpawnFxAtom(int clipIdx, TrackNotify notify, CompositeEffectEntry entry)
        {
            Transform socket = FindSocket(entry.Socket);
            // ParentToSpawnerRoot: 소켓 위치에서 스폰하되 부모는 캐릭터 루트(_target). 배치는 매 프레임 PlaceFxAtom가.
            bool toRoot = entry.ParentToSpawnerRoot && _target != null;
            Transform parent = toRoot ? _target.transform : socket;

            var go = Instantiate(entry.Prefab, parent);
            go.hideFlags = HideFlags.DontSave;

            var top = new List<ParticleSystem>();
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!EffectEditorShared.HasParticleAncestor(ps.transform, go.transform)) top.Add(ps);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            var a = new FxPreviewAtom { Root = go, Top = top, ClipIdx = clipIdx, Notify = notify, Entry = entry, Socket = socket };
            if (!toRoot) ApplyFxTransform(a);   // 루트 부모형은 활성 진입 시 캡처해 배치
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
                    Quaternion frame = a.Entry.IgnoreSocketRotation ? root.rotation : socket.rotation;
                    Vector3    wpos = a.Entry.IgnoreSocketRotation
                        ? socket.position + frame * a.Entry.PositionOffset
                        : socket.TransformPoint(a.Entry.PositionOffset);
                    Quaternion wrot = frame * Quaternion.Euler(a.Entry.EulerOffset);
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
            ApplyFxTransform(a);
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
                    // 구조 필드(프리팹/소켓) — 변경 시 재생성
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(prefabProp, new GUIContent("Prefab"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("Socket"), new GUIContent("Socket"));
                    bool structural = EditorGUI.EndChangeCheck();

                    // 실시간 필드
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("StartDelay"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("Duration"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("PlaybackSpeed"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("PositionOffset"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("EulerOffset"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("Scale"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("FollowSpawner"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("ParentToSpawnerRoot"),
                        new GUIContent("Parent To Spawner Root", "손 위치에서 스폰하되 캐릭터 루트에 붙임 — 손 스윙 무시, 캐릭터 이동/방향만 따라감"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("IgnoreSocketRotation"),
                        new GUIContent("Ignore Socket Rotation", "소켓 위치만 쓰고 회전은 무시(월드 기준). 본에 회전이 구워져 EulerOffset 조준이 어려울 때"));

                    EffectEditorShared.DrawParamOverrides(e, prefab);

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
                _fxCompositeSO.ApplyModifiedProperties();
                _fxDirty = true;
            }

            _fxCompositeSO.ApplyModifiedProperties();
        }
    }
}
