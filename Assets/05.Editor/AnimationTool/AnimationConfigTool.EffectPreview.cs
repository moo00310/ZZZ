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
        }

        private readonly List<FxPreviewAtom> _fxAtoms = new List<FxPreviewAtom>();
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
                if (!active) continue;

                ApplyFxTransform(a);   // 오프셋/스케일 편집 실시간 반영
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
            Transform parent = FindSocket(entry.Socket);

            var go = Instantiate(entry.Prefab, parent);
            go.hideFlags = HideFlags.DontSave;

            var top = new List<ParticleSystem>();
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!EffectEditorShared.HasParticleAncestor(ps.transform, go.transform)) top.Add(ps);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            var a = new FxPreviewAtom { Root = go, Top = top, ClipIdx = clipIdx, Notify = notify, Entry = entry };
            ApplyFxTransform(a);
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

        // ── Effect 탭 인스펙터 ────────────────────────────────────
        private void DrawEffectInspector(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.2f, 0.2f, 0.2f));
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f));
            _inspScroll = EditorGUILayout.BeginScrollView(_inspScroll);

            EditorGUILayout.LabelField("Effect Sync", EditorStyles.boldLabel);

            if (_target == null || _config == null)
            {
                EditorGUILayout.HelpBox("상단에서 Target(캐릭터)과 Config를 지정하세요.", MessageType.Info);
                EndEffectInspector();
                return;
            }
            if (_comboMode)
            {
                EditorGUILayout.HelpBox("Combo 모드에선 이펙트 프리뷰가 비활성입니다.\n" +
                    "Play 바의 Combo 토글을 끄고 순차 모드로 보세요.", MessageType.Warning);
                EndEffectInspector();
                return;
            }

            EditorGUILayout.LabelField($"스폰 인스턴스: {_fxAtoms.Count}개", EditorStyles.miniLabel);
            if (GUILayout.Button("Rebuild Effects")) _fxDirty = true;
            EditorGUILayout.Space(4f);

            bool valid = _selectedNotify >= 0 && _notifyClipIdx >= 0 && _notifyClipIdx < _config.Clips.Count
                         && _selectedNotify < _config.Clips[_notifyClipIdx].Notifies.Count;
            if (!valid)
            {
                EditorGUILayout.HelpBox("타임라인에서 Effect Notify(아이콘 E)를 선택하면 여기서 편집합니다.",
                    MessageType.None);
                EndEffectInspector();
                return;
            }

            var clip   = _config.Clips[_notifyClipIdx];
            var notify = clip.Notifies[_selectedNotify];

            EditorGUILayout.LabelField(
                $"{Short(clip.Clip != null ? clip.Clip.name : "(clip)")}  ·  Notify #{_selectedNotify}",
                EditorStyles.miniBoldLabel);

            if (notify.Type != NotifyType.Effect)
            {
                EditorGUILayout.HelpBox("선택한 Notify가 Effect 타입이 아닙니다.", MessageType.Info);
                EndEffectInspector();
                return;
            }

            // 발동 시점 — 애니 프레임 기준
            EditorGUI.BeginChangeCheck();
            float nt = EditorGUILayout.Slider("발동 시점 (0~1)", notify.NormalizedTime, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Edit Notify Time");
                notify.NormalizedTime = nt;
                EditorUtility.SetDirty(_config);
            }
            if (clip.Clip != null && clip.Clip.frameRate > 0f)
            {
                int frame = Mathf.RoundToInt(notify.NormalizedTime * clip.Clip.length * clip.Clip.frameRate);
                int total = Mathf.RoundToInt(clip.Clip.length * clip.Clip.frameRate);
                EditorGUILayout.LabelField(" ", $"{frame} / {total} f");
            }

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

            if (notify.Effect != null)
            {
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
            }
            else EditorGUILayout.HelpBox("CompositeEffect를 지정하거나 New로 생성하세요.", MessageType.Info);

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

            EndEffectInspector();
        }

        private void EndEffectInspector()
        {
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // 조합 Entry들 — 프리팹 + 스폰 위치(소켓/오프셋/스케일) + 조합 내 시차 + 풀링/반납.
        // 프리팹/소켓 변경은 구조 변경이라 재생성(_fxDirty), 그 외는 실시간 반영.
        private void DrawCompositeEntries(CompositeEffect composite)
        {
            if (_fxComposite != composite || _fxCompositeSO == null)
            {
                _fxComposite   = composite;
                _fxCompositeSO = new SerializedObject(composite);
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
                if (newExp != expanded) { if (newExp) _fxEntryCollapsed.Remove(i); else _fxEntryCollapsed.Add(i); }
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

                    // 풀링/반납 — 접기
                    bool poolFold = _fxPoolFold.Contains(i);
                    bool newPool = EditorGUILayout.Foldout(poolFold, "풀링/반납 설정", true);
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
                e.FindPropertyRelative("PrewarmCount").intValue = 4;
                e.FindPropertyRelative("MaxSize").intValue = 0;
                e.FindPropertyRelative("Despawn").enumValueIndex = (int)DespawnMode.ParticleStopped;
                e.FindPropertyRelative("Lifetime").floatValue = 0f;
                _fxCompositeSO.ApplyModifiedProperties();
                _fxDirty = true;
            }

            _fxCompositeSO.ApplyModifiedProperties();
        }
    }
}
