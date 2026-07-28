using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ;
using ZZZ.Effects;
using ZZZ.Player.StateMachine;

namespace ZZZ.Editor.AnimationTool
{
    public partial class AnimationConfigTool
    {
        // ── 우측 인스펙터 ────────────────────────────────────────
        private void DrawInspector(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.2f, 0.2f, 0.2f));
            GUILayout.BeginArea(area);

            bool clipSelected = _config != null &&
                _selectedClip >= 0 && _selectedClip < _config.Clips.Count;

            // 상단 버튼 — Track / Global Links 인스펙터 토글. 빈 공간 클릭이 아니라 이 버튼으로만 연다.
            using (new EditorGUI.DisabledScope(_config == null))
            {
                bool want = GUILayout.Toggle(_showTrack && !clipSelected,
                    "▤  Track / Global Links", EditorStyles.toolbarButton);
                if (want && (!_showTrack || clipSelected))
                {
                    _showTrack      = true;   // 트랙 뷰 진입 → 클립 선택 해제
                    _selectedClip   = -1;
                    _selectedNotify = -1;
                    clipSelected    = false;
                }
                else if (!want) _showTrack = false;
            }

            // 좁은 패널에 맞춰 라벨 폭 축소 + 세로 전용 스크롤(가로 스크롤바 제거 → 내용이 폭에 맞춰 줄어듦)
            float prevLabelW = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(120f, area.width * 0.4f);
            _inspScroll = EditorGUILayout.BeginScrollView(
                _inspScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            if (_config == null)
            {
                EditorGUILayout.LabelField("Config를 선택하세요.", EditorStyles.centeredGreyMiniLabel);
            }
            else if (clipSelected)
            {
                _showTrack = false;   // 클립을 보는 동안엔 트랙 뷰 해제 (해제 시 빈 화면으로 복귀)
                var tc = _config.Clips[_selectedClip];
                if (_selectedNotify >= 0 && _notifyClipIdx == _selectedClip &&
                    _selectedNotify < tc.Notifies.Count)
                    DrawNotifyInspector(tc, _selectedNotify);
                else
                    DrawClipInspector(tc, _selectedClip);
            }
            else if (_showTrack)
            {
                DrawTrackLevelInspector();
            }
            else
            {
                EditorGUILayout.LabelField("클립을 선택하거나,", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.LabelField("위 [Track / Global Links] 버튼을 누르세요.",
                    EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = prevLabelW;   // 전역 라벨 폭 원복
            GUILayout.EndArea();
        }

        private void DrawTrackLevelInspector()
        {
            EditorGUILayout.LabelField("Track", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            string trackName = EditorGUILayout.TextField("Name", _config.TrackName);

            // Entry Section 드롭다운 (시작 섹션)
            string[] opts = BuildSectionOptions(_config);   // [0] = (End/Entry) = 첫 클립
            int cur = Mathf.Max(0, Array.IndexOf(opts,
                string.IsNullOrEmpty(_config.EntrySection) ? "(End/Entry)" : _config.EntrySection));
            int sel = EditorGUILayout.Popup("Entry Section", cur, ShortAll(opts));

            // DoneThreshold는 config 전역값 → Entry/첫 섹션 프레임 기준으로 표시 (0=클립 마지막 프레임 자동)
            TrackClip doneRef = ReferenceClip();
            float done = doneRef != null
                ? FrameField("OnEnd 발동 (0=끝)", "OnEnd 링크가 발동하는 프레임 (0=클립 마지막 프레임 자동). 기준: Entry/첫 섹션", doneRef, _config.DoneThreshold)
                : EditorGUILayout.Slider("OnEnd 발동 (0=마지막프레임)", _config.DoneThreshold, 0f, 1f);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Edit Track");
                _config.TrackName      = trackName;
                _config.EntrySection   = sel == 0 ? "" : opts[sel];
                _config.DoneThreshold  = done;
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.LabelField($"Clips: {_config.Clips.Count}  /  Total: {GetTotalDuration():F2}s",
                EditorStyles.miniLabel);
            EditorGUILayout.HelpBox("콤보 Play는 [선택한 클립] → [Entry Section] → [첫 클립] 순으로 시작합니다.",
                MessageType.None);

            // ── Global Links (모든 클립에 적용 = Any State 전이) ──
            DrawSeparator();
            EditorGUILayout.LabelField($"Global Links  ({_config.GlobalLinks.Count})",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "여기 링크는 이 config의 모든 섹션에서 평가됩니다 (Any State).\n" +
                "예: 이동 입력 시 어디서든 Walk로. 클립마다 달 필요 없음.\n" +
                "평가 순서: 각 클립 고유 Links → Global Links.",
                MessageType.None);
            DrawLinksEditor(_config.GlobalLinks);

            DrawSeparator();
            EditorGUILayout.LabelField("Root Motion (프리뷰 전용)", EditorStyles.boldLabel);

            // PlayerController가 있으면 _bip001Bone/_rootMotionScale은 거기서 자동 추출된다(AutoDetectRootBones).
            // 그 경우 수동 입력칸은 무의미(덮어써짐)하므로 읽기전용 표시만, 없을 때만 수동 오버라이드 노출.
            bool autoDetected = _target != null
                && _target.GetComponentInChildren<ZZZ.Player.PlayerController>() != null;
            if (autoDetected)
            {
                string boneLabel = _bip001Bone != null ? _bip001Bone.name : "미설정";
                EditorGUILayout.LabelField("자동 감지",
                    $"Bip001: {boneLabel}  ·  RM×{_rootMotionScale:0.##}  (PlayerController)",
                    EditorStyles.miniLabel);
            }
            else
            {
                // PlayerController 없는 타겟 → 수동 오버라이드(폴백)
                _bip001Bone = (Transform)EditorGUILayout.ObjectField("Bip001 Bone", _bip001Bone, typeof(Transform), true);
                _rootMotionScale = EditorGUILayout.FloatField("RM Scale", _rootMotionScale);
            }

            EditorGUILayout.HelpBox(
                "클립 Move Mode = RootMotion이면\nBip001 이동량이 프리뷰 캐릭터에 적용됩니다 (런타임/빌드엔 무관).",
                MessageType.None);

            DrawSeparator();
            EditorGUILayout.LabelField("클립 바 클릭 → 편집", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField("우클릭 → Notify 추가",  EditorStyles.centeredGreyMiniLabel);
        }

        // 섹션 모듈 리스트 (i-frame 등 폴리모픽). 있는 모듈만 표시/편집.
        private void DrawModules(TrackClip tc)
        {
            DrawSeparator();
            bool expanded = DrawInspectorSectionHeader(
                $"Modules  ({tc.Modules.Count})", _expandedInspectorModules.Contains(tc));
            if (expanded)
                _expandedInspectorModules.Add(tc);
            else
                _expandedInspectorModules.Remove(tc);

            if (!expanded) return;

            int removeAt = -1;
            for (int i = 0; i < tc.Modules.Count; i++)
            {
                var m = tc.Modules[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(m != null ? m.DisplayName : "(null)", GUILayout.Width(170));
                if (GUILayout.Button("−", GUILayout.Width(24))) removeAt = i;
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                float windowStart = 0f, windowEnd = 0f;
                if (m is WindowModule wm)   // 윈도우 모듈(무적/패링/…) 공용 — Start/End 슬라이더
                {
                    windowStart = wm.Start;
                    windowEnd = wm.End;
                    FrameWindowField("   Window (f)", "이 모듈이 작동하는 프레임 구간",
                        tc, ref windowStart, ref windowEnd);
                }

                float distance = m is AdditionalMovementModule move ? move.Distance : 0f;
                AdditionalMoveDirection moveDirection = m is AdditionalMovementModule moveDir
                    ? moveDir.Direction : AdditionalMoveDirection.Forward;
                float stopDistance = m is TargetWarpModule warp ? warp.StopDistance : 0f;
                float turnSpeed = m is FaceTargetModule face ? face.TurnSpeed : 0f;
                float boostSpeed = m is StartBoostModule boost ? boost.Speed : 0f;
                float boostDuration = m is StartBoostModule boostTime ? boostTime.Duration : 0f;
                float backScale = m is BackMotionScaleModule back ? back.Scale : 0f;

                if (m is AdditionalMovementModule)
                {
                    distance = EditorGUILayout.FloatField(
                        new GUIContent("   Distance", "Window 전체에 걸쳐 추가할 총 이동 거리(m)"), distance);
                    moveDirection = (AdditionalMoveDirection)EditorGUILayout.EnumPopup(
                        new GUIContent("   Direction", "Forward/Backward는 캐릭터 기준, MoveInput은 현재 입력 방향"),
                        moveDirection);
                }
                else if (m is TargetWarpModule)
                    stopDistance = EditorGUILayout.FloatField("   Stop Distance", stopDistance);
                else if (m is FaceTargetModule)
                    turnSpeed = EditorGUILayout.FloatField("   Turn Speed (°/s)", turnSpeed);
                else if (m is StartBoostModule)
                {
                    boostSpeed = EditorGUILayout.FloatField("   Speed", boostSpeed);
                    boostDuration = EditorGUILayout.FloatField("   Duration (s)", boostDuration);
                }
                else if (m is BackMotionScaleModule)
                    backScale = EditorGUILayout.FloatField("   Scale", backScale);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_config, "Edit Module");
                    if (m is WindowModule window)
                    {
                        window.Start = Mathf.Clamp01(Mathf.Min(windowStart, windowEnd));
                        window.End = Mathf.Clamp01(Mathf.Max(windowStart, windowEnd));
                    }
                    if (m is AdditionalMovementModule movement)
                    {
                        movement.Distance = distance;
                        movement.Direction = moveDirection;
                    }
                    else if (m is TargetWarpModule targetWarp)
                        targetWarp.StopDistance = Mathf.Max(0f, stopDistance);
                    else if (m is FaceTargetModule faceTarget)
                        faceTarget.TurnSpeed = Mathf.Max(0f, turnSpeed);
                    else if (m is StartBoostModule startBoost)
                    {
                        startBoost.Speed = Mathf.Max(0f, boostSpeed);
                        startBoost.Duration = Mathf.Max(0f, boostDuration);
                    }
                    else if (m is BackMotionScaleModule backMotion)
                        backMotion.Scale = Mathf.Max(0f, backScale);
                    EditorUtility.SetDirty(_config);
                }
            }

            if (removeAt >= 0)
            {
                Undo.RecordObject(_config, "Remove Module");
                tc.Modules.RemoveAt(removeAt);
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Add:", GUILayout.Width(34));
            // 등록된 모든 SectionModule 타입을 드롭다운으로 나열 — 새 모듈 추가 = 클래스 1개 추가뿐.
            if (GUILayout.Button("＋ 모듈 선택", EditorStyles.popup, GUILayout.Width(160)))
                ShowAddModuleMenu(tc);
            EditorGUILayout.EndHorizontal();
        }

        // SectionModule을 상속한 모든(비추상) 타입을 메뉴로 띄워 추가. 이미 있는 타입은 비활성.
        private void ShowAddModuleMenu(TrackClip tc)
        {
            var menu = new GenericMenu();
            foreach (var t in TypeCache.GetTypesDerivedFrom<SectionModule>())
            {
                if (t.IsAbstract) continue;
                var sample = (SectionModule)System.Activator.CreateInstance(t);
                var label  = new GUIContent(sample.MenuName);

                if (tc.Modules.Exists(x => x != null && x.GetType() == t))
                    menu.AddDisabledItem(new GUIContent(sample.MenuName + "  (이미 있음)"));
                else
                {
                    var type = t;   // 클로저 캡처
                    menu.AddItem(label, false, () =>
                    {
                        Undo.RecordObject(_config, "Add Module");
                        tc.Modules.Add((SectionModule)System.Activator.CreateInstance(type));
                        EditorUtility.SetDirty(_config);
                    });
                }
            }
            menu.ShowAsContext();
        }

        private void DrawClipInspector(TrackClip tc, int idx)
        {
            EditorGUILayout.LabelField($"Clip  {idx + 1}", EditorStyles.boldLabel);
            DrawSeparator();

            EditorGUI.BeginChangeCheck();
            string sect = EditorGUILayout.TextField("Section Name", tc.SectionName);
            var   clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", tc.Clip, typeof(AnimationClip), false);
            float spd  = EditorGUILayout.FloatField("Speed",       tc.Speed);
            var   mode = (MoveMode)EditorGUILayout.EnumPopup("Move Mode", tc.MoveMode);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Edit Clip");
                // 섹션 이름이 비었으면 클립 이름으로 자동 채움
                if (string.IsNullOrEmpty(sect) && clip != null) sect = clip.name;
                tc.SectionName = sect;
                tc.Clip = clip;
                tc.Speed = Mathf.Max(0.01f, spd);
                tc.MoveMode = mode;
                EditorUtility.SetDirty(_config);
            }

            // Loop은 클립 임포트 설정(Loop Time)에서 자동 표시 — 편집 불가
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle(new GUIContent("Loop (클립 설정)",
                    "클립 임포트의 Loop Time 설정을 그대로 표시 (config에서 관리 안 함)"), tc.IsLooping);

            DrawModules(tc);

            if (tc.Clip != null)
                EditorGUILayout.LabelField(
                    $"{tc.Clip.length:F3}s → {tc.Clip.length / Mathf.Max(0.01f, tc.Speed):F3}s   " +
                    $"({Mathf.RoundToInt(tc.Clip.length * tc.Clip.frameRate)}f)",
                    EditorStyles.miniLabel);

            // ── Links (다음 섹션 분기) ───────────────────────────
            DrawSeparator();
            bool linksExpanded = DrawInspectorSectionHeader(
                $"Links  ({tc.Links.Count})", _expandedInspectorLinks.Contains(tc));
            if (linksExpanded)
            {
                _expandedInspectorLinks.Add(tc);
                EditorGUILayout.LabelField("링크 헤더 클릭=펼치기+강조 / ▲▼=순서", EditorStyles.miniLabel);
                DrawLinksEditor(tc.Links, idx);
            }
            else
            {
                _expandedInspectorLinks.Remove(tc);
            }

            DrawSeparator();
            EditorGUILayout.LabelField($"Notifies  ({tc.Notifies.Count})  —  우클릭 추가",
                EditorStyles.miniLabel);
        }

        private static bool DrawInspectorSectionHeader(string title, bool expanded)
        {
            var style = new GUIStyle(EditorStyles.foldoutHeader)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
            };
            return EditorGUILayout.Foldout(expanded, title, true, style);
        }

        // ownerClip >= 0 이면 링크 선택 가능(타임라인에 그 링크만 강조). -1 = Global 등 비선택.
        private void DrawLinksEditor(List<ClipLink> links, int ownerClip = -1)
        {
            bool selectable = ownerClip >= 0;
            if (selectable && _linkOwnerClip != ownerClip) { _selectedLink = -1; _linkOwnerClip = ownerClip; }
            if (_selectedLink >= links.Count) _selectedLink = -1;

            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                EditorGUILayout.BeginVertical("box");

                bool isSel    = selectable && _selectedLink == i;
                bool expanded = !selectable || isSel;   // Global은 항상 펼침, clip 링크는 포커스 시 펼침

                // ── 헤더: 접기/펼치기(=강조) + 순서 이동(▲▼) + 삭제(×) ──
                EditorGUILayout.BeginHorizontal();

                // 접기/선택 (▼/▶ + 번호)
                if (selectable)
                {
                    var foldStyle = new GUIStyle(EditorStyles.boldLabel)
                    { normal = { textColor = isSel ? Color.white : new Color(0.82f, 0.82f, 0.82f) } };
                    if (GUILayout.Button($"{(expanded ? "▼" : "▶")} {i + 1}.", foldStyle, GUILayout.Width(34)))
                        _selectedLink = isSel ? -1 : i;
                }
                else GUILayout.Label($"{i + 1}.", EditorStyles.boldLabel, GUILayout.Width(24));

                // 윗줄: 대상 이름(강조) + 순서/삭제 버튼(오른쪽 끝)
                GUILayout.Label("→ " +
                    (string.IsNullOrEmpty(link.TargetSection) ? "End/복귀" : Short(link.TargetSection)),
                    new GUIStyle(EditorStyles.boldLabel)
                    { fontSize = 13, clipping = TextClipping.Clip,
                      normal = { textColor = isSel ? Color.white : LinkColor(link) } });

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("▲", GUILayout.Width(20)))
                    {
                        MoveLink(links, i, i - 1);
                        EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); break;
                    }
                using (new EditorGUI.DisabledScope(i == links.Count - 1))
                    if (GUILayout.Button("▼", GUILayout.Width(20)))
                    {
                        MoveLink(links, i, i + 1);
                        EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); break;
                    }
                // 복사 — 이 링크를 클립보드에 담아 다른 섹션/모든 섹션에 붙여넣기
                if (GUILayout.Button(new GUIContent("⧉", "이 링크 복사 (아래 '붙여넣기'로 다른 섹션에)"),
                        GUILayout.Width(22)))
                    _linkClipboard = CloneLink(link);
                if (GUILayout.Button("×", GUILayout.Width(20)))
                {
                    Undo.RecordObject(_config, "Remove Link");
                    links.RemoveAt(i);
                    if (_selectedLink == i) _selectedLink = -1;
                    else if (_selectedLink > i) _selectedLink--;
                    EditorUtility.SetDirty(_config);
                    Repaint();
                    EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); break;
                }
                EditorGUILayout.EndHorizontal();

                // 아랫줄: 조건 칩 (카테고리별 색) — 번호 폭만큼 들여쓰기.
                // Attack=파랑 / Direction=초록 / When=주황. None/Any/기본 타이밍은 생략.
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(34f);
                var chipIc = ReadInput(link);   // 표시 전용 — InputCondition이 아니면 null
                if (chipIc != null)
                {
                    if (chipIc.Attack != ComboInput.None)
                        DrawChip(chipIc.Attack.ToString(), k_chipAttack);
                    if (chipIc.Direction != MoveDir.Any)
                        DrawChip(chipIc.Direction.ToString(), k_chipDir);
                }
                else if (link.Condition != null)   // 비입력 조건(Always/몬스터 등)
                    DrawChip(link.Condition.DisplayName, k_chipAttack);
                if (link.Timing == LinkTiming.OnRelease)
                    DrawChip("release", k_chipWhenMiss);
                else if (link.Timing == LinkTiming.OnEnd)
                    DrawChip("end", k_chipWhen);
                else if (link.Timing == LinkTiming.OnEndIfMatched)
                    DrawChip("win→end", k_chipWhen);
                bool unconditional = link.Condition == null || link.Condition is AlwaysCondition
                    || (chipIc != null && chipIc.Attack == ComboInput.None && chipIc.Direction == MoveDir.Any);
                if (unconditional && link.Timing == LinkTiming.WhenMatched)
                    DrawChip("무조건", new Color(0.5f, 0.5f, 0.5f));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (expanded)
                {
                    // ── 조건 타입 (다형성) — 타입 교체는 즉시 반영(아래 입력 필드 write-back과 분리) ──
                    DrawConditionTypePicker(link);

                    EditorGUI.BeginChangeCheck();

                    // ── 대상 ──
                    var targetCfg = (AnimationConfig)EditorGUILayout.ObjectField(
                        "Target Config", link.TargetConfig, typeof(AnimationConfig), false);
                    var      cfgForSections = targetCfg != null ? targetCfg : _config;
                    string[] sectionOptions = BuildSectionOptions(cfgForSections);
                    int curIdx = Mathf.Max(0, Array.IndexOf(sectionOptions,
                        string.IsNullOrEmpty(link.TargetSection) ? "(End/Entry)" : link.TargetSection));
                    int newIdx = EditorGUILayout.Popup("→ Section", curIdx, ShortAll(sectionOptions));

                    // 입력 조건 편집 — Condition이 InputCondition일 때만 노출(다형성). 타 타입(Always 등)은 필드 없음.
                    InputCondition curIc = link.Condition as InputCondition;
                    ComboInput attack      = curIc?.Attack      ?? ComboInput.None;
                    MoveDir    dir         = curIc?.Direction   ?? MoveDir.Any;
                    bool       requireHeld = curIc?.RequireHeld ?? false;
                    if (curIc != null)
                    {
                        attack = (ComboInput)ColoredEnum(new GUIContent("Attack"), k_chipAttack, attack);
                        // 특정 공격 키일 때만 의미 — 그 키가 '눌려있는 동안' 조건 충족 (차지 루프용)
                        if (attack != ComboInput.None && attack != ComboInput.Any)
                            requireHeld = EditorGUILayout.Toggle(
                                new GUIContent("  Require Held", "체크 시 이 키가 '지금 눌려있을(held) 때' 충족 (누름 버퍼 대신 홀드). OnEnd 자기-루프+EntryOffset과 함께 차지 루프"),
                                requireHeld);
                        dir = (MoveDir)ColoredEnum(new GUIContent("Direction"), k_chipDir, dir);
                    }
                    var timing = (LinkTiming)ColoredEnum(
                        new GUIContent("When", TimingHelp(link.Timing)), k_chipWhen, link.Timing);

                    // Window = 현재(owner) 섹션 기준 프레임 / Entry Offset = 대상 섹션 기준 프레임
                    TrackClip ownerTc = (ownerClip >= 0 && ownerClip < _config.Clips.Count)
                        ? _config.Clips[ownerClip] : null;

                    float ws = link.WindowStart, we = link.WindowEnd;
                    if (timing != LinkTiming.OnEnd)   // OnEnd는 윈도우 불필요
                    {
                        if (ownerTc != null)
                            FrameWindowField("Window (f)", "이 섹션 재생 중 이 프레임 구간에서 조건을 평가", ownerTc, ref ws, ref we);
                        else   // Global Links 등 owner 클립이 없으면 normalized 슬라이더 폴백 (섹션마다 길이가 달라 프레임 불가)
                            EditorGUILayout.MinMaxSlider(new GUIContent($"Window {ws:F2}-{we:F2}"), ref ws, ref we, 0f, 1f);
                    }

                    float blend = EditorGUILayout.FloatField("Blend (s)", link.BlendDuration);

                    // Entry Offset은 '대상 섹션'을 그 지점부터 재생 → 대상 클립의 프레임 기준
                    var       offCfg    = targetCfg != null ? targetCfg : _config;
                    int       offSecIdx = offCfg != null ? offCfg.IndexOfSection(link.TargetSection) : -1;
                    TrackClip offTc     = (offCfg != null && offSecIdx >= 0) ? offCfg.Clips[offSecIdx] : null;
                    float     entryOff;
                    if (offTc != null)
                        entryOff = FrameField("Entry Offset (f)", "전이 후 대상 섹션을 이 프레임부터 재생 (0=처음부터, 윈드업 스킵). 이 지점 이전 Notify는 생략", offTc, link.EntryOffset);
                    else
                        entryOff = EditorGUILayout.Slider(new GUIContent($"Entry Offset {link.EntryOffset:F2}", "전이 후 대상 섹션을 이 normalizedTime 지점부터 재생 (0=처음부터)"), link.EntryOffset, 0f, 1f);

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Edit Link");
                        link.TargetConfig  = targetCfg;
                        link.TargetSection = newIdx == 0 ? "" : sectionOptions[newIdx];
                        if (curIc != null)   // 입력 조건일 때만 입력 필드 반영 — 타 타입(Always/몬스터) 클로버링 금지
                        {
                            curIc.Attack      = attack;
                            curIc.RequireHeld = requireHeld;
                            curIc.Direction   = dir;
                        }
                        link.Timing        = timing;
                        link.WindowStart   = ws;
                        link.WindowEnd     = we;
                        link.BlendDuration = Mathf.Max(0f, blend);
                        link.EntryOffset   = Mathf.Clamp01(entryOff);
                        EditorUtility.SetDirty(_config);
                    }
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Link 추가"))
                AddLink(links, new ClipLink());

            using (new EditorGUI.DisabledScope(_linkClipboard == null))
            {
                string clipDesc = _linkClipboard != null
                    ? "→ " + (string.IsNullOrEmpty(_linkClipboard.TargetSection)
                              ? "End/복귀" : Short(_linkClipboard.TargetSection))
                    : "복사된 링크 없음";

                // 이 목록에 클립보드 링크 1개 추가
                if (GUILayout.Button(new GUIContent("⧉ 붙여넣기", $"복사한 링크 추가  ({clipDesc})"),
                        GUILayout.Width(90)))
                    AddLink(links, CloneLink(_linkClipboard));
            }
            EditorGUILayout.EndHorizontal();
        }

        // 링크 클립보드 — 한 링크를 복사해 다른 섹션/모든 섹션에 붙여넣기 (창 세션 동안 유지)
        private static ClipLink _linkClipboard;

        // ClipLink 깊은 복사 — 값 필드 전부 복사, TargetConfig는 참조 그대로(에셋 공유). Condition은 깊은 복사.
        private static ClipLink CloneLink(ClipLink s) => new ClipLink
        {
            TargetConfig  = s.TargetConfig,
            TargetSection = s.TargetSection,
            BlendDuration = s.BlendDuration,
            EntryOffset   = s.EntryOffset,
            Condition     = s.Condition?.Clone(),
            Timing        = s.Timing,
            WindowStart   = s.WindowStart,
            WindowEnd     = s.WindowEnd,
        };

        // ── 조건 타입(다형성) 선택 ──────────────────────────────────
        // LinkCondition 하위 타입을 팝업으로 나열해 링크마다 조건 타입을 고른다. 타입 교체는 새 인스턴스로
        // 즉시 반영(입력 필드 write-back과 분리) — 그래야 Always/몬스터 조건이 InputCondition으로 덮이지 않는다.
        private void DrawConditionTypePicker(ClipLink link)
        {
            EnsureConditionTypes();
            // Condition이 null이면 런타임 Always 폴백과 동일하므로 Always로 표시한다.
            Type cur = link.Condition != null ? link.Condition.GetType() : typeof(AlwaysCondition);
            int idx = 0;
            for (int i = 0; i < s_condTypes.Length; i++)
                if (s_condTypes[i] == cur) { idx = i; break; }

            int sel = EditorGUILayout.Popup(
                new GUIContent("Condition", "전이 조건 타입 (다형성) — 타입에 따라 아래 필드가 달라진다"),
                idx, s_condNames);
            if (sel != idx)
            {
                Undo.RecordObject(_config, "Change Condition Type");
                link.Condition = (LinkCondition)Activator.CreateInstance(s_condTypes[sel]);
                EditorUtility.SetDirty(_config);
            }
        }

        // LinkCondition 구상 타입 + 표시 이름(MenuName) 캐시 — TypeCache로 1회 수집(타입명순 안정 정렬).
        private static Type[]   s_condTypes;
        private static string[] s_condNames;
        private static void EnsureConditionTypes()
        {
            if (s_condTypes != null) return;
            var types = new List<Type>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<LinkCondition>())
                if (!t.IsAbstract) types.Add(t);
            types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            s_condTypes = types.ToArray();
            s_condNames = new string[s_condTypes.Length];
            for (int i = 0; i < s_condTypes.Length; i++)
                s_condNames[i] = ((LinkCondition)Activator.CreateInstance(s_condTypes[i])).MenuName;
        }

        // 표시/시드용 입력 조건 읽기 — Condition이 InputCondition이면 그것, 아니면(Always/몬스터/null) null.
        private static InputCondition ReadInput(ClipLink link) => link.Condition as InputCondition;

        // ── 프레임 단위 입력 헬퍼 ─────────────────────────────────────
        // 데이터는 normalizedTime(0~1)으로 저장하되, 인스펙터에선 클립 프레임 수 기준 정수 프레임으로 표시/편집한다.
        // owner 클립의 총 프레임(길이×frameRate)을 분모로 normalized↔frame 변환. clip이 없으면 1프레임으로 폴백.
        private static int ClipFrames(TrackClip owner)
            => (owner != null && owner.Clip != null && owner.Clip.frameRate > 0f)
                ? Mathf.Max(1, Mathf.RoundToInt(owner.Clip.length * owner.Clip.frameRate)) : 1;

        // 프레임 단위 Min-Max 입력 — normStart/normEnd(0~1)를 정수 프레임 두 칸으로 편집. 저장은 normalized.
        private static void FrameWindowField(string label, string tooltip, TrackClip owner,
            ref float normStart, ref float normEnd)
        {
            int frames = ClipFrames(owner);
            int fs = Mathf.Clamp(Mathf.RoundToInt(normStart * frames), 0, frames);
            int fe = Mathf.Clamp(Mathf.RoundToInt(normEnd   * frames), 0, frames);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(label, tooltip), GUILayout.Width(EditorGUIUtility.labelWidth));
            fs = EditorGUILayout.IntField(fs, GUILayout.Width(44));
            GUILayout.Label("~", GUILayout.Width(10));
            fe = EditorGUILayout.IntField(fe, GUILayout.Width(44));
            GUILayout.Label($"/ {frames}f", EditorStyles.miniLabel, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            normStart = Mathf.Clamp01((float)Mathf.Clamp(fs, 0, frames) / frames);
            normEnd   = Mathf.Clamp01((float)Mathf.Clamp(fe, 0, frames) / frames);
        }

        // 프레임 단위 단일 값 입력 — norm(0~1)을 정수 프레임 한 칸으로 편집. 저장은 normalized.
        private static float FrameField(string label, string tooltip, TrackClip owner, float norm)
        {
            int frames = ClipFrames(owner);
            int f = Mathf.Clamp(Mathf.RoundToInt(norm * frames), 0, frames);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(label, tooltip), GUILayout.Width(EditorGUIUtility.labelWidth));
            f = EditorGUILayout.IntField(f, GUILayout.Width(44));
            GUILayout.Label($"/ {frames}f", EditorStyles.miniLabel, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            return Mathf.Clamp01((float)Mathf.Clamp(f, 0, frames) / frames);
        }

        // DoneThreshold(config 전역) 프레임 표시용 기준 클립 — Entry 섹션, 없으면 첫 클립.
        private TrackClip ReferenceClip()
        {
            if (_config == null || _config.Clips.Count == 0) return null;
            int i = _config.IndexOfSection(_config.EntrySection);
            return _config.Clips[i >= 0 ? i : 0];
        }

        // 칩 카테고리 색 (Attack=파랑 / Direction=초록 / When=주황·빨강)
        private static readonly Color k_chipAttack   = new Color(0.30f, 0.62f, 1.00f);
        private static readonly Color k_chipDir      = new Color(0.40f, 0.80f, 0.48f);
        private static readonly Color k_chipWhen     = new Color(0.95f, 0.70f, 0.30f);
        private static readonly Color k_chipWhenMiss = new Color(0.95f, 0.45f, 0.32f);

        // 헤더용 색 칩(pill) — 짧은 텍스트 + 색 배경 + 명도 대비 글자색
        private static void DrawChip(string text, Color col)
        {
            Vector2 sz = EditorStyles.miniLabel.CalcSize(new GUIContent(text));
            Rect r = GUILayoutUtility.GetRect(sz.x + 9f, 16f, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(new Rect(r.x + 1f, r.y + 1f, r.width - 1f, r.height - 2f),
                new Color(col.r, col.g, col.b, 0.9f));
            float lum = 0.299f * col.r + 0.587f * col.g + 0.114f * col.b;
            GUI.Label(r, text, new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleCenter, fontSize = 9,
              normal = { textColor = lum > 0.6f ? Color.black : Color.white } });
        }

        // 색 라벨 + EnumPopup — 칩과 같은 카테고리 색으로 라벨을 칠해 본문에서도 구분 쉽게
        private static Enum ColoredEnum(GUIContent label, Color col, Enum value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, new GUIStyle(EditorStyles.label)
            { normal = { textColor = col } }, GUILayout.Width(EditorGUIUtility.labelWidth));
            Enum result = EditorGUILayout.EnumPopup(value);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        // 링크 순서 swap (▲▼). 포커스 인덱스도 같이 따라가게 보정.
        private void MoveLink(List<ClipLink> links, int from, int to)
        {
            if (to < 0 || to >= links.Count) return;
            Undo.RecordObject(_config, "Reorder Link");
            (links[from], links[to]) = (links[to], links[from]);
            if      (_selectedLink == from) _selectedLink = to;
            else if (_selectedLink == to)   _selectedLink = from;
            EditorUtility.SetDirty(_config);
            Repaint();
        }

        private void AddLink(List<ClipLink> links, ClipLink link)
        {
            Undo.RecordObject(_config, "Add Link");
            if (link.Condition == null) link.Condition = new InputCondition();   // 새 링크 기본 = 입력 조건
            links.Add(link);
            EditorUtility.SetDirty(_config);
            Repaint();
        }

        // 링크 조건을 짧은 문자열로 (타임라인/인스펙터 공용)
        private static string CondLabel(ClipLink link)
        {
            string cond = "";
            var ic = ReadInput(link);
            if (ic != null)
            {
                if (ic.Attack != ComboInput.None) cond = ic.Attack.ToString();
                if (ic.Direction != MoveDir.Any)
                    cond = string.IsNullOrEmpty(cond) ? ic.Direction.ToString()
                                                      : cond + "+" + ic.Direction;
            }
            else if (link.Condition != null && !(link.Condition is AlwaysCondition))
                cond = link.Condition.DisplayName;
            if (string.IsNullOrEmpty(cond)) cond = "무조건";

            string suffix = link.Timing switch
            {
                LinkTiming.OnRelease      => " (release)",
                LinkTiming.OnEnd          => " (end)",
                LinkTiming.OnEndIfMatched => " (win→end)",
                _                          => "",
            };
            return cond + suffix;
        }

        private static string TimingHelp(LinkTiming t) => t switch
        {
            LinkTiming.WhenMatched    => "윈도우 안에서 조건 충족 시 즉시 전이",
            LinkTiming.OnRelease      => "윈도우 안에서 Attack 키를 떼면 전이 (홀드 차지 → 릴리스)",
            LinkTiming.OnEnd          => "클립이 끝나면 전이 (루프 클립 제외)",
            LinkTiming.OnEndIfMatched => "윈도우 안에 조건이 한 번이라도 충족되면 래치 → 섹션 끝에 전이 (카운터 예약 등)",
            _                          => "",
        };

        // [0] = "(End/Entry)" + 해당 config의 모든 섹션 이름
        private string[] BuildSectionOptions(AnimationConfig cfg)
        {
            var list = new System.Collections.Generic.List<string> { "(End/Entry)" };
            if (cfg == null) return list.ToArray();
            foreach (var c in cfg.Clips)
            {
                string n = !string.IsNullOrEmpty(c.SectionName) ? c.SectionName
                         : c.Clip != null ? c.Clip.name : "";
                if (!string.IsNullOrEmpty(n) && !list.Contains(n)) list.Add(n);
            }
            return list.ToArray();
        }

        private void DrawNotifyInspector(TrackClip tc, int ni)
        {
            var notify = tc.Notifies[ni];
            EditorGUILayout.LabelField($"Notify  —  {notify.Type}", EditorStyles.boldLabel);

            // 이 클립의 Notify 목록 — 타임라인에서 겹쳐 집기 어려울 때 여기서 확실히 선택.
            // (프레임 순으로 정렬해 버튼으로 나열, 현재 선택은 강조)
            DrawNotifyPicker(tc);
            DrawSeparator();

            EditorGUI.BeginChangeCheck();
            var   type   = (NotifyType)EditorGUILayout.EnumPopup("Type",  notify.Type);
            bool  locked = EditorGUILayout.Toggle(
                new GUIContent("Lock (이동 잠금)", "켜면 타임라인에서 드래그로 시점이 밀리지 않는다. 선택·편집·삭제는 그대로 가능."),
                notify.Locked);
            // Lock 중에도 Time 필드로는 값을 미세 조정할 수 있게 둔다 — 막는 건 '드래그 이동'뿐.
            float normT  = FrameField("Time (f)", "이 Notify가 발동하는 프레임", tc, notify.NormalizedTime);
            string eName = EditorGUILayout.TextField("Event Name",   notify.EventName);
            float endT = notify.EndNormalizedTime;
            EffectTransitionMode transitionMode = notify.TransitionMode;
            string nextSection = notify.NextSection;
            if (type == NotifyType.Effect)
            {
                endT = FrameField("구간 끝 (f)",
                    "0 또는 Time 이하 = 단발. Time보다 크면 그 프레임까지 유지되는 구간 이펙트(트레일/오라). 섹션 이탈·캔슬 시 자동 정지",
                    tc, notify.EndNormalizedTime);
                if (endT > normT)
                    EditorGUILayout.LabelField(" ", "구간 이펙트 (유지 중 방출 → 끝에서 정지)",
                        EditorStyles.miniLabel);
                transitionMode = (EffectTransitionMode)EditorGUILayout.EnumPopup(
                    new GUIContent("Transition Effect",
                        "Keep: Notify에서 재생 후 자연 소멸. Stop: Notify에서 재생 후 현재 상태 이탈 시 정지. Next: 지정한 다음 섹션으로 실제 전환될 때만 재생하고 그 섹션 이탈 시 정지."),
                    notify.TransitionMode);
                if (transitionMode == EffectTransitionMode.Next)
                {
                    var sectionOptions = new List<string> { "" };
                    for (int i = 0; i < tc.Links.Count; i++)
                    {
                        string targetSection = tc.Links[i].TargetSection;
                        if (!string.IsNullOrEmpty(targetSection)
                            && !sectionOptions.Contains(targetSection))
                            sectionOptions.Add(targetSection);
                    }
                    if (!string.IsNullOrEmpty(nextSection)
                        && !sectionOptions.Contains(nextSection))
                        sectionOptions.Add(nextSection);

                    string[] sectionLabels = new string[sectionOptions.Count];
                    sectionLabels[0] = "(Select Section)";
                    for (int i = 1; i < sectionOptions.Count; i++)
                        sectionLabels[i] = sectionOptions[i];
                    int selectedSection = Mathf.Max(0, sectionOptions.IndexOf(nextSection));
                    selectedSection = EditorGUILayout.Popup(
                        new GUIContent("Next Section",
                            "실제 전환 목적지가 이 섹션일 때만 이펙트 소유권을 전달합니다."),
                        selectedSection, sectionLabels);
                    nextSection = sectionOptions[selectedSection];
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                bool typeChanged = type != notify.Type;
                Undo.RecordObject(_config, "Edit Notify");
                notify.Type = type; notify.NormalizedTime = normT;
                notify.EventName = eName;
                notify.EndNormalizedTime = endT; notify.Locked = locked;
                notify.TransitionMode = transitionMode;
                notify.NextSection = nextSection;
                EditorUtility.SetDirty(_config);
                if (typeChanged) _fxDirty = true;   // Effect↔다른 타입 전환 시 프리뷰 재생성
            }

            // Effect 타입이면 조합(Composite) 편집 + 씬 프리뷰를 여기서 인라인으로 (별도 탭 없음)
            if (notify.Type == NotifyType.Effect)
            {
                DrawSeparator();
                DrawEffectSection(tc, notify);
            }

            DrawSeparator();
            GUI.backgroundColor = new Color(0.72f, 0.22f, 0.22f);
            if (GUILayout.Button("Delete Notify", GUILayout.Width(120)))
            {
                Undo.RecordObject(_config, "Delete Notify");
                tc.Notifies.RemoveAt(ni);
                _selectedNotify = -1;
                EditorUtility.SetDirty(_config);
                _serializedConfig = new SerializedObject(_config);
                Repaint();
            }
            GUI.backgroundColor = Color.white;
        }

        // 선택 클립의 모든 Notify를 프레임 순 칩으로 나열 — 겹쳐서 타임라인 클릭이 어려울 때
        // 여기서 확실히 골라 선택한다. 칩 라벨 = [타입머리글자][프레임], 잠금은 '·' 표기.
        private void DrawNotifyPicker(TrackClip tc)
        {
            if (tc.Notifies.Count <= 1) return;   // 하나뿐이면 굳이 안 그림

            float clipFrames = tc.Clip != null ? tc.Clip.length * 30f : 0f;   // 표시용 프레임(30fps 가정)

            // 프레임 순 인덱스
            var order = new List<int>(tc.Notifies.Count);
            for (int k = 0; k < tc.Notifies.Count; k++) order.Add(k);
            order.Sort((a, b) => tc.Notifies[a].NormalizedTime.CompareTo(tc.Notifies[b].NormalizedTime));

            EditorGUILayout.LabelField($"이 클립의 Notify ({tc.Notifies.Count})", EditorStyles.miniLabel);

            int perRow = Mathf.Max(1, Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 24f) / 52f));
            for (int p = 0; p < order.Count; p++)
            {
                if (p % perRow == 0) EditorGUILayout.BeginHorizontal();

                int idx = order[p];
                var n   = tc.Notifies[idx];
                string head = n.Type switch
                {
                    NotifyType.Effect => "E", NotifyType.Camera => "C",
                    NotifyType.Sound  => "S", _ => "N",
                };
                int f = Mathf.RoundToInt(n.NormalizedTime * clipFrames);
                string label = (n.Locked ? "·" : "") + $"{head}{f}";

                bool cur = idx == _selectedNotify && _notifyClipIdx == _selectedClip;
                Color prev = GUI.backgroundColor;
                if (cur) GUI.backgroundColor = new Color(0.95f, 0.85f, 0.25f);
                if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(48f)))
                {
                    _selectedNotify = idx; _notifyClipIdx = _selectedClip;
                    GUI.FocusControl(null);
                    Repaint();
                }
                GUI.backgroundColor = prev;

                if (p % perRow == perRow - 1 || p == order.Count - 1) EditorGUILayout.EndHorizontal();
            }
        }

        private static void DrawSeparator()
        {
            EditorGUILayout.Space(2);
            var r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.3f, 0.3f, 0.3f, 0.5f));
        }
    }
}
