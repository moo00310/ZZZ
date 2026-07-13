using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ.Effects;

namespace ZZZ.Editor.Effects
{
    // EffectTool(독립 창)과 AnimationConfigTool의 Effect 탭이 공유하는 그리기/계산 로직.
    // 원자 = 프리팹 직접 참조 + Entry에 저장된 설정. 별도 EffectDefinition SO는 없다.
    internal static class EffectEditorShared
    {
        private static readonly Color[] BarColors =
        {
            new Color(1.0f, 0.55f, 0.20f), new Color(0.30f, 0.75f, 1.0f),
            new Color(0.35f, 0.85f, 0.45f), new Color(0.85f, 0.40f, 0.85f),
            new Color(0.95f, 0.80f, 0.30f), new Color(0.55f, 0.55f, 0.95f),
        };

        // ── 길이(초) 근사 ──
        public static float PrefabDuration(GameObject prefab)
        {
            if (prefab == null) return 0f;
            float max = 0f;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main;
                float d = m.duration + m.startLifetime.constantMax;
                if (d > max) max = d;
            }
            return max;
        }

        // Entry의 유효 재생 길이(초) — 프리팹 원래 길이에 PlaybackSpeed(배율)와 Duration(방출 컷)을 반영한 근사.
        // (Duration 컷 후 잔여 파티클 소멸 꼬리는 무시한다 — 타임라인 표시/프리뷰용)
        public static float EntryDuration(CompositeEffectEntry e)
        {
            if (e == null || e.Prefab == null) return 0f;
            float speed   = e.PlaybackSpeed > 0f ? e.PlaybackSpeed : 1f;
            float natural = PrefabNaturalDuration(e) / speed;
            return e.Duration > 0f ? Mathf.Min(natural, e.Duration) : natural;
        }

        // StartLifetime 오버라이드(>0)를 반영한 프리팹 길이 근사. 단일 PS 기준으로
        // duration + (오버라이드 수명 or 구운 수명). 오버라이드 없으면(0) PrefabDuration과 동일.
        private static float PrefabNaturalDuration(CompositeEffectEntry e)
        {
            if (e.StartLifetime <= 0f) return PrefabDuration(e.Prefab);
            float baseDur = 0f;
            var ps = e.Prefab.GetComponentInChildren<ParticleSystem>(true);
            if (ps != null) baseDur = ps.main.duration;
            return baseDur + e.StartLifetime;
        }

        public static float CompositeDuration(CompositeEffect c)
        {
            if (c == null) return 0f;
            float max = 0f;
            foreach (var e in c.Entries)
            {
                if (e == null || e.Prefab == null) continue;
                float end = e.StartDelay + EntryDuration(e);
                if (end > max) max = end;
            }
            return Mathf.Max(max, 0.5f);
        }

        public static bool HasParticleAncestor(Transform t, Transform root)
        {
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                if (p.GetComponent<ParticleSystem>() != null) return true;
                if (p == root) break;
            }
            return false;
        }

        // ── Entry의 풀링/반납 설정 + 검증 ──
        // entryProp = CompositeEffectEntry의 SerializedProperty. prefab은 검증용.
        public static void DrawEntryPoolFields(SerializedProperty entryProp, GameObject prefab)
        {
            // 풀 프리웜/상한은 프리팹의 EffectPoolConfig에서 프리팹 단위로 — 여기선 반납만.
            EditorGUILayout.LabelField("Despawn", EditorStyles.miniBoldLabel);
            var despawn = entryProp.FindPropertyRelative("Despawn");
            EditorGUILayout.PropertyField(despawn);
            if (despawn.enumValueIndex == (int)DespawnMode.Fixed)
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("Lifetime"));

            DrawStopActionValidation(prefab, (DespawnMode)despawn.enumValueIndex);
        }

        private static void DrawStopActionValidation(GameObject prefab, DespawnMode mode)
        {
            if (prefab == null || mode != DespawnMode.ParticleStopped) return;

            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0)
            {
                EditorGUILayout.HelpBox("프리팹에 ParticleSystem이 없어 ParticleStopped 반납이 동작하지 않습니다.",
                    MessageType.Warning);
                return;
            }
            bool anyBad = false;
            foreach (var ps in systems)
            {
                if (HasParticleAncestor(ps.transform, prefab.transform)) continue;
                if (ps.main.stopAction != ParticleSystemStopAction.Callback) anyBad = true;
            }
            if (anyBad)
                EditorGUILayout.HelpBox(
                    "최상위 ParticleSystem 중 Stop Action이 Callback이 아닌 것이 있습니다.\n" +
                    "ParticleStopped 자동 반납이 안 될 수 있습니다 (Main → Stop Action = Callback).",
                    MessageType.Warning);
        }

        // ── 셰이더 노브 오버라이드 (프리팹이 EffectParameterSet으로 선언한 것만 동적으로 그림) ──
        // entryProp = CompositeEffectEntry의 SerializedProperty. prefab에서 선언을 읽고,
        // 값은 entry.ParamOverrides에 upsert한다(체크 = 오버라이드, 해제 = 프리팹 기본값).
        public static void DrawParamOverrides(SerializedProperty entryProp, GameObject prefab)
        {
            if (prefab == null) return;
            var set = prefab.GetComponent<EffectParameterSet>();
            if (set == null || set.Parameters.Count == 0) return;

            var overrides = entryProp.FindPropertyRelative("ParamOverrides");

            foreach (var p in set.Parameters)
            {
                if (p == null || string.IsNullOrEmpty(p.ShaderProperty)) continue;

                int  idx = FindOverrideIndex(overrides, p.ShaderProperty);
                bool has = idx >= 0;

                EditorGUILayout.BeginHorizontal();
                string label = string.IsNullOrEmpty(p.DisplayName) ? p.ShaderProperty : p.DisplayName;
                bool nowHas = EditorGUILayout.ToggleLeft(new GUIContent(label, p.ShaderProperty), has,
                    GUILayout.Width(150));
                if (nowHas != has)
                {
                    if (nowHas) idx = AddOverride(overrides, p);
                    else { overrides.DeleteArrayElementAtIndex(idx); idx = -1; }
                    has = nowHas;
                }

                if (has)
                {
                    var el = overrides.GetArrayElementAtIndex(idx);
                    var fv = el.FindPropertyRelative("FloatValue");
                    var cv = el.FindPropertyRelative("ColorValue");
                    switch (p.Type)
                    {
                        case EffectParamType.Bool:
                            fv.floatValue = EditorGUILayout.Toggle(fv.floatValue > 0.5f) ? 1f : 0f;
                            break;
                        case EffectParamType.Color:
                            cv.colorValue = EditorGUILayout.ColorField(cv.colorValue);
                            break;
                        default: // Float
                            var rndProp = el.FindPropertyRelative("Randomize");
                            if (rndProp.boolValue)
                            {
                                // 매 재생 랜덤 — 값은 런타임 Bind가 굴리므로 여기선 범위만 표시.
                                string range = p.HasRange ? $"{p.Min:0.##}~{p.Max:0.##}" : "0~100";
                                using (new EditorGUI.DisabledScope(true))
                                    EditorGUILayout.LabelField($"런타임 랜덤 ({range})");
                            }
                            else
                            {
                                fv.floatValue = p.HasRange
                                    ? EditorGUILayout.Slider(fv.floatValue, p.Min, p.Max)
                                    : EditorGUILayout.FloatField(fv.floatValue);
                            }
                            rndProp.boolValue = GUILayout.Toggle(rndProp.boolValue,
                                new GUIContent("랜덤", "켜면 런타임에서 재생마다 범위 내 랜덤값을 사용"),
                                EditorStyles.miniButton, GUILayout.Width(46));
                            break;
                    }
                }
                else
                {
                    // 오버라이드 안 함 → 프리팹 기본값을 흐리게 보여줘 뭘 덮게 되는지 알려준다.
                    using (new EditorGUI.DisabledScope(true))
                    {
                        switch (p.Type)
                        {
                            case EffectParamType.Bool:  EditorGUILayout.Toggle(p.DefaultFloat > 0.5f); break;
                            case EffectParamType.Color: EditorGUILayout.ColorField(p.DefaultColor);    break;
                            default:                    EditorGUILayout.FloatField(p.DefaultFloat);    break;
                        }
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private static int FindOverrideIndex(SerializedProperty overrides, string shaderProperty)
        {
            for (int i = 0; i < overrides.arraySize; i++)
                if (overrides.GetArrayElementAtIndex(i).FindPropertyRelative("ShaderProperty").stringValue == shaderProperty)
                    return i;
            return -1;
        }

        // 리스트 끝에 오버라이드 항목 추가 + 프리팹 선언 기본값으로 초기화.
        private static int AddOverride(SerializedProperty overrides, EffectParamDecl decl)
        {
            int n = overrides.arraySize;
            overrides.InsertArrayElementAtIndex(n);
            var el = overrides.GetArrayElementAtIndex(n);
            el.FindPropertyRelative("ShaderProperty").stringValue = decl.ShaderProperty;
            el.FindPropertyRelative("Type").enumValueIndex        = (int)decl.Type;
            el.FindPropertyRelative("FloatValue").floatValue      = decl.DefaultFloat;
            el.FindPropertyRelative("ColorValue").colorValue      = decl.DefaultColor;
            el.FindPropertyRelative("Randomize").boolValue        = false;
            return n;
        }

        // ── Entry 필드 확정 표시 순서 (두 툴 공용) ──
        // Prefab → Socket → StartDelay/PlaybackSpeed → [Option] → [파티클 노브] → [쉐이더 노브] (그룹은 접기 가능).
        // 구조 필드(Prefab/Socket) 변경 여부를 리턴 — 호출 툴이 프리뷰 재생성 트리거로 쓴다.
        public static bool DrawEntryFields(SerializedProperty e, SerializedProperty prefabProp, GameObject prefab)
        {
            // 구조 필드 — 변경 시 재생성
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(prefabProp, new GUIContent("Prefab"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("Socket"), new GUIContent("Socket"));
            bool structural = EditorGUI.EndChangeCheck();

            // 룩 통째 교체 — null이면 프리팹 기본 머티리얼(렌더러 sharedMaterial 스왑)
            EditorGUILayout.PropertyField(e.FindPropertyRelative("MaterialOverride"),
                new GUIContent("Material", "비우면 프리팹 기본. 지정하면 렌더러 머티리얼을 통째 교체(텍스처+색+파라미터). 같은 셰이더 공유 권장"));

            // 기본 타이밍(항상 표시)
            EditorGUILayout.PropertyField(e.FindPropertyRelative("StartDelay"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("PlaybackSpeed"));

            // Option — 부착/추종 방식
            if (FoldGroup(e, "Option"))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(e.FindPropertyRelative("FollowSpawner"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("ParentToSpawnerRoot"),
                    new GUIContent("Parent To Spawner Root", "손 위치에서 스폰하되 캐릭터 루트에 붙임 — 손 스윙 무시, 캐릭터 이동/방향만 따라감"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("IgnoreSocketRotation"),
                    new GUIContent("Ignore Socket Rotation", "소켓 위치만 쓰고 회전은 무시(월드 기준). 본에 회전이 구워져 EulerOffset 조준이 어려울 때"));
                EditorGUI.indentLevel--;
            }

            // 파티클 노브 — 타이밍/배치 + 모양/색(단일 PS의 조합별 델타)
            if (FoldGroup(e, "파티클 노브"))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(e.FindPropertyRelative("Duration"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("StartLifetime"),
                    new GUIContent("Start Lifetime", "0 = 프리팹 기본값(안 덮음). >0이면 덮어써 나오고 사라지는 전체 속도 조절 — 작을수록 빠른 번쩍"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("PositionOffset"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("EulerOffset"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("Scale"));
                DrawParticleShapeRows(e, prefab);
                EditorGUI.indentLevel--;
            }

            // 쉐이더 노브 — 프리팹이 EffectParameterSet으로 선언한 게 있을 때만 그룹 노출
            var shaderSet = prefab != null ? prefab.GetComponent<EffectParameterSet>() : null;
            if (shaderSet != null && shaderSet.Parameters.Count > 0 && FoldGroup(e, "쉐이더 노브"))
            {
                EditorGUI.indentLevel++;
                DrawParamOverrides(e, prefab);
                EditorGUI.indentLevel--;
            }

            return structural;
        }

        // 그룹 접기 상태 — 순수 UI라 공유 메서드가 자체 보관(대상 인스턴스ID + 프로퍼티경로 + 그룹명 키).
        // 없으면 펼침(기본). 도메인 리로드 시 초기화되는 건 무방.
        private static readonly HashSet<string> _groupCollapsed = new HashSet<string>();

        private static bool FoldGroup(SerializedProperty e, string label)
        {
            string key = e.serializedObject.targetObject.GetInstanceID() + e.propertyPath + label;
            bool expanded = !_groupCollapsed.Contains(key);
            bool now = EditorGUILayout.Foldout(expanded, label, true);
            if (now != expanded) { if (now) _groupCollapsed.Remove(key); else _groupCollapsed.Add(key); }
            return now;
        }

        // ── 파티클 모듈 토글 노브 (단일 PS의 조합별 델타: Size커브/시작색) ──
        // 셰이더 노브와 달리 프리팹 선언이 필요 없다(파티클 모듈은 어느 PS에나 있는 보편 노브).
        // 커브/색은 무해한 중립값이 없어 토글 = sparse: 끈 필드는 런타임에서 프리팹 기본값(baseline)으로 되돌린다.
        // (StartLifetime은 '0=중립'이라 토글 없이 Entry 일반 필드로 그린다 — DrawEntryFields 참조)
        public static void DrawParticleShapeRows(SerializedProperty entryProp, GameObject prefab)
        {
            var root = entryProp.FindPropertyRelative("ParticleOverride");
            if (root == null) return;
            if (prefab != null && prefab.GetComponentInChildren<ParticleSystem>(true) == null) return;

            // Size over Lifetime — 커브 왼쪽=나오는 속도, 오른쪽=사라지는 속도
            var onSize = root.FindPropertyRelative("OverrideSizeCurve");
            EditorGUILayout.BeginHorizontal();
            onSize.boolValue = EditorGUILayout.ToggleLeft(
                new GUIContent("Size o/Life", "크기 커브. 왼쪽 기울기=나오는 속도, 오른쪽 기울기=사라지는 속도, 피크 위치=번쩍 타이밍"),
                onSize.boolValue, GUILayout.Width(150));
            using (new EditorGUI.DisabledScope(!onSize.boolValue))
            {
                var mult = root.FindPropertyRelative("SizeMultiplier");
                mult.floatValue = EditorGUILayout.FloatField(mult.floatValue, GUILayout.Width(44));
                var curve = root.FindPropertyRelative("SizeCurve");
                curve.animationCurveValue = EditorGUILayout.CurveField(
                    curve.animationCurveValue, Color.cyan, new Rect(0f, 0f, 1f, 1f));
            }
            EditorGUILayout.EndHorizontal();

            // Start Color (HDR) — 밝기/색
            var onCol = root.FindPropertyRelative("OverrideStartColor");
            EditorGUILayout.BeginHorizontal();
            onCol.boolValue = EditorGUILayout.ToggleLeft(
                new GUIContent("Start Color", "시작 색(HDR). Intensity를 올리면 블룸이 터진다"),
                onCol.boolValue, GUILayout.Width(150));
            using (new EditorGUI.DisabledScope(!onCol.boolValue))
            {
                var c = root.FindPropertyRelative("StartColor");
                c.colorValue = EditorGUILayout.ColorField(GUIContent.none, c.colorValue, true, true, true);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── 조합 내부 StartDelay 타임라인 ──
        public static void DrawStartDelayTimeline(Rect area, CompositeEffect c,
            ref int dragEntry, ref float grabDx, float playheadTime = -1f, System.Action<float> onRulerScrub = null)
        {
            EditorGUI.DrawRect(area, new Color(0.16f, 0.16f, 0.16f));
            if (c == null) return;

            const float rulerH = 16f, rowH = 20f, labelW = 100f;
            float total    = CompositeDuration(c);
            float timeX     = area.x + labelW;
            float timeW     = area.width - labelW - 10f;
            float pxPerSec  = Mathf.Clamp(timeW / total, 30f, 400f);

            var rulerRect = new Rect(timeX, area.y, timeW, rulerH);
            EditorGUI.DrawRect(rulerRect, new Color(0.12f, 0.12f, 0.12f));
            for (float t = 0f; t <= total + 0.001f; t += 0.1f)
            {
                float x = timeX + t * pxPerSec;
                bool major = Mathf.Abs(t * 10f % 5f) < 0.01f;
                float h = major ? rulerH : rulerH * 0.5f;
                EditorGUI.DrawRect(new Rect(x, area.y + rulerH - h, 1f, h), new Color(1f, 1f, 1f, 0.25f));
                if (major) GUI.Label(new Rect(x + 2f, area.y - 1f, 40f, rulerH), $"{t:0.0}", EditorStyles.miniLabel);
            }

            float y = area.y + rulerH + 2f;
            for (int i = 0; i < c.Entries.Count; i++)
            {
                var e = c.Entries[i];
                if (i % 2 == 1) EditorGUI.DrawRect(new Rect(area.x, y, area.width, rowH), new Color(1f, 1f, 1f, 0.02f));

                string label = e != null && e.Prefab != null ? e.Prefab.name : "(none)";
                GUI.Label(new Rect(area.x + 4f, y + 2f, labelW - 6f, rowH - 2f), label, EditorStyles.miniLabel);

                if (e != null && e.Prefab != null)
                {
                    float dur  = Mathf.Max(EntryDuration(e), 0.05f);
                    float barX = timeX + e.StartDelay * pxPerSec;
                    float barW = Mathf.Max(dur * pxPerSec, 8f);
                    var barRect = new Rect(barX, y + 3f, barW, rowH - 6f);

                    Color col = BarColors[i % BarColors.Length];
                    if (dragEntry == i || dragEntry == ResizeId(i)) col = Color.Lerp(col, Color.white, 0.3f);
                    EditorGUI.DrawRect(barRect, col);

                    string info = $"{e.StartDelay:0.##}s";
                    if (e.Duration > 0f) info += $" ▸{e.Duration:0.##}s";
                    if (e.PlaybackSpeed > 0f && !Mathf.Approximately(e.PlaybackSpeed, 1f)) info += $" ×{e.PlaybackSpeed:0.##}";
                    GUI.Label(new Rect(barX + 3f, y + 3f, barW - 4f, rowH - 6f), info, EditorStyles.whiteMiniLabel);

                    // 우측 엣지 = Duration 리사이즈 그립 (이동 드래그보다 먼저 처리해 이벤트를 선점)
                    var gripRect = new Rect(barRect.xMax - 5f, barRect.y, 10f, barRect.height);
                    EditorGUI.DrawRect(new Rect(barRect.xMax - 3f, barRect.y, 3f, barRect.height), new Color(1f, 1f, 1f, 0.35f));
                    HandleBarResize(c, i, gripRect, barX, pxPerSec, ref dragEntry);
                    HandleBarDrag(c, i, barRect, timeX, pxPerSec, ref dragEntry, ref grabDx);
                    EditorGUIUtility.AddCursorRect(gripRect, MouseCursor.ResizeHorizontal);
                    EditorGUIUtility.AddCursorRect(new Rect(barRect.x, barRect.y, barRect.width - 5f, barRect.height), MouseCursor.SlideArrow);
                }
                y += rowH;
            }

            if (playheadTime >= 0f)
            {
                float px = timeX + playheadTime * pxPerSec;
                EditorGUI.DrawRect(new Rect(px, area.y, 1.5f, area.height), new Color(1f, 0.9f, 0.2f, 0.9f));
            }

            if (onRulerScrub != null)
            {
                var ev = Event.current;
                if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag)
                    && ev.button == 0 && rulerRect.Contains(ev.mousePosition))
                {
                    onRulerScrub(Mathf.Max(0f, (ev.mousePosition.x - timeX) / pxPerSec));
                    ev.Use();
                }
            }
        }

        // 리사이즈 드래그는 이동(양수 index)과 같은 dragEntry 상태를 공유하되 음수로 인코딩해 구분한다 (-1 = 없음).
        private static int ResizeId(int index) => -2 - index;

        // 막대 우측 엣지 드래그 → Entry.Duration(방출 컷)을 굽는다. 원래 길이 이상으로 늘리면 0(자연 길이)으로 복귀.
        private static void HandleBarResize(CompositeEffect c, int index, Rect gripRect, float barX, float pxPerSec,
            ref int dragEntry)
        {
            int id = ResizeId(index);
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && gripRect.Contains(e.mousePosition))
                    {
                        dragEntry = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (dragEntry == id)
                    {
                        var entry = c.Entries[index];
                        float d = (e.mousePosition.x - barX) / pxPerSec;
                        if (e.control || e.command) d = Mathf.Round(d * 20f) / 20f;   // 0.05s 스냅
                        float speed   = entry.PlaybackSpeed > 0f ? entry.PlaybackSpeed : 1f;
                        float natural = PrefabDuration(entry.Prefab) / speed;
                        Undo.RecordObject(c, "Edit Effect Duration");
                        entry.Duration = d >= natural - 0.01f ? 0f : Mathf.Max(0.05f, d);
                        EditorUtility.SetDirty(c);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (dragEntry == id) { dragEntry = -1; e.Use(); }
                    break;
            }
        }

        private static void HandleBarDrag(CompositeEffect c, int index, Rect barRect, float timeX, float pxPerSec,
            ref int dragEntry, ref float grabDx)
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && barRect.Contains(e.mousePosition))
                    {
                        dragEntry = index;
                        grabDx    = e.mousePosition.x - barRect.x;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (dragEntry == index)
                    {
                        float nd = (e.mousePosition.x - grabDx - timeX) / pxPerSec;
                        if (e.control || e.command) nd = Mathf.Round(nd * 20f) / 20f;   // 0.05s 스냅
                        nd = Mathf.Max(0f, nd);
                        Undo.RecordObject(c, "Edit Effect Start Delay");
                        c.Entries[index].StartDelay = nd;
                        EditorUtility.SetDirty(c);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (dragEntry == index) { dragEntry = -1; e.Use(); }
                    break;
            }
        }

        // ── 풀 개요 테이블 (플레이 모드) ──
        public static void DrawPoolTable(ref Vector2 scroll)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("Prefab",  EditorStyles.miniBoldLabel, GUILayout.MinWidth(120));
            GUILayout.Label("Free",    EditorStyles.miniBoldLabel, GUILayout.Width(46));
            GUILayout.Label("Live",    EditorStyles.miniBoldLabel, GUILayout.Width(46));
            GUILayout.Label("Created", EditorStyles.miniBoldLabel, GUILayout.Width(56));
            GUILayout.Label("Max",     EditorStyles.miniBoldLabel, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(160f));
            bool any = false;
            foreach (EffectPool pool in EffectService.DebugPools)
            {
                any = true;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(pool.Prefab != null ? pool.Prefab.name : "(null)", GUILayout.MinWidth(120));
                GUILayout.Label(pool.FreeCount.ToString(),    GUILayout.Width(46));
                GUILayout.Label(pool.LiveCount.ToString(),    GUILayout.Width(46));
                GUILayout.Label(pool.CreatedCount.ToString(), GUILayout.Width(56));
                GUILayout.Label(pool.MaxSize > 0 ? pool.MaxSize.ToString() : "∞", GUILayout.Width(40));
                EditorGUILayout.EndHorizontal();
            }
            if (!any) EditorGUILayout.HelpBox("아직 생성된 풀이 없습니다. 이펙트가 재생되면 나타납니다.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        // ── 에셋 생성 ──
        public static T CreateAsset<T>(string defaultName) where T : ScriptableObject
        {
            string folder = "Assets/02.Effects";
            var sel = Selection.activeObject;
            if (sel != null)
            {
                string p = AssetDatabase.GetAssetPath(sel);
                if (!string.IsNullOrEmpty(p))
                    folder = AssetDatabase.IsValidFolder(p) ? p : System.IO.Path.GetDirectoryName(p);
            }
            if (!AssetDatabase.IsValidFolder(folder)) folder = "Assets";

            var asset = ScriptableObject.CreateInstance<T>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{defaultName}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
