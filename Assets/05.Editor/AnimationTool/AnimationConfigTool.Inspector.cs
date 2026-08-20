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
        private bool _editCameraShotEndPose;

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
            EditorGUILayout.HelpBox(
                "Move Mode = RootMotion이면 AnimationClip의 RootT/RootQ를 읽어 " +
                "런타임 OnAnimatorMove와 같은 방식으로 프리뷰합니다. " +
                "Section Turn은 샘플링된 Bip001-Root 상대 회전을 사용합니다.",
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
                bool keepInitialDirection = m is AdditionalMovementModule initialMove
                    && initialMove.KeepInitialDirection;
                float stopDistance = m is TargetWarpModule warp ? warp.StopDistance : 0f;
                float turnSpeed = m is FaceTargetModule face ? face.TurnSpeed : 0f;
                bool smoothEntry = m is FaceTargetModule smoothFace
                    && smoothFace.SmoothEntry;
                float boostSpeed = m is StartBoostModule boost ? boost.Speed : 0f;
                float boostDuration = m is StartBoostModule boostTime ? boostTime.Duration : 0f;
                float backScale = m is BackMotionScaleModule back ? back.Scale : 0f;
                RootMotionRotationAxis sourceAxis = m is SectionTurnModule turnAxis
                    ? turnAxis.SourceAxis : RootMotionRotationAxis.Auto;
                float rotationScale = m is SectionTurnModule turn ? turn.RotationScale : 1f;
                float targetAngle = m is SectionTurnModule turnTarget ? turnTarget.TargetAngle : 0f;

                if (m is AdditionalMovementModule)
                {
                    distance = EditorGUILayout.FloatField(
                        new GUIContent("   Distance", "Window 전체에 걸쳐 추가할 총 이동 거리(m)"), distance);
                    moveDirection = (AdditionalMoveDirection)EditorGUILayout.EnumPopup(
                        new GUIContent("   Direction", "Forward/Backward는 캐릭터 기준, MoveInput은 현재 입력 방향"),
                        moveDirection);
                    keepInitialDirection = EditorGUILayout.Toggle(
                        new GUIContent("   시작 방향 유지",
                            "체크하면 섹션 진입 순간의 이동 방향을 유지합니다. 섹션 턴 중에도 이동 궤적이 함께 회전하지 않습니다."),
                        keepInitialDirection);
                }
                else if (m is TargetWarpModule)
                    stopDistance = EditorGUILayout.FloatField("   Stop Distance", stopDistance);
                else if (m is FaceTargetModule)
                {
                    turnSpeed = EditorGUILayout.FloatField("   Turn Speed (°/s)", turnSpeed);
                    smoothEntry = EditorGUILayout.Toggle(
                        new GUIContent("   Smooth Entry",
                            "진입 즉시 스냅하지 않고 Window 동안 Turn Speed로 회전합니다."),
                        smoothEntry);
                }
                else if (m is StartBoostModule)
                {
                    boostSpeed = EditorGUILayout.FloatField("   Speed", boostSpeed);
                    boostDuration = EditorGUILayout.FloatField("   Duration (s)", boostDuration);
                }
                else if (m is BackMotionScaleModule)
                    backScale = EditorGUILayout.FloatField("   Scale", backScale);
                else if (m is SectionTurnModule)
                {
                    sourceAxis = (RootMotionRotationAxis)EditorGUILayout.EnumPopup(
                        new GUIContent("   Source Axis",
                            "Bip001 프레임 델타 - Root 프레임 델타에서 턴 각도로 사용할 축. Auto는 프레임별 주축을 선택합니다."),
                        sourceAxis);
                    rotationScale = EditorGUILayout.FloatField("   Rotation Scale", rotationScale);
                    targetAngle = EditorGUILayout.FloatField("   Target Angle", targetAngle);
                }

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
                        movement.KeepInitialDirection = keepInitialDirection;
                    }
                    else if (m is TargetWarpModule targetWarp)
                        targetWarp.StopDistance = Mathf.Max(0f, stopDistance);
                    else if (m is FaceTargetModule faceTarget)
                    {
                        faceTarget.TurnSpeed = Mathf.Max(0f, turnSpeed);
                        faceTarget.SmoothEntry = smoothEntry;
                    }
                    else if (m is StartBoostModule startBoost)
                    {
                        startBoost.Speed = Mathf.Max(0f, boostSpeed);
                        startBoost.Duration = Mathf.Max(0f, boostDuration);
                    }
                    else if (m is BackMotionScaleModule backMotion)
                        backMotion.Scale = Mathf.Max(0f, backScale);
                    else if (m is SectionTurnModule sectionTurn)
                    {
                        sectionTurn.SourceAxis = sourceAxis;
                        sectionTurn.RotationScale = Mathf.Max(0f, rotationScale);
                        sectionTurn.TargetAngle = Mathf.Max(0f, targetAngle);
                    }
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

        private static float DurationFrameField(string label, string tooltip,
            TrackClip owner, float seconds)
        {
            float frameRate = 30f;
            if (owner?.Clip != null && owner.Clip.frameRate > 0f)
            {
                float playbackSpeed = Mathf.Max(0.01f, Mathf.Abs(owner.Speed));
                frameRate = owner.Clip.frameRate * playbackSpeed;
            }

            int frames = Mathf.Max(0, Mathf.RoundToInt(seconds * frameRate));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(label, tooltip),
                GUILayout.Width(EditorGUIUtility.labelWidth));
            frames = Mathf.Max(0, EditorGUILayout.IntField(
                frames, GUILayout.Width(44)));
            GUILayout.Label(
                $"@ {frameRate:0.##} fps", EditorStyles.miniLabel,
                GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
            return frames / frameRate;
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
            if (notify.MigratePayload()) EditorUtility.SetDirty(_config);
            HitOrigin previousHitOrigin = notify.Hit?.Origin ?? HitOrigin.CharacterRoot;
            string previousEffectKey = notify.Hit?.EffectKey ?? "";
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
            string eName = notify.EventName;
            if (type == NotifyType.Sound || type == NotifyType.Custom)
                eName = EditorGUILayout.TextField("Event Name", notify.EventName);

            CameraNotifyPayload cameraPayload =
                notify.Payload as CameraNotifyPayload;
            CameraNotifyMode cameraMode =
                cameraPayload?.Mode ?? CameraNotifyMode.Shake;
            float cameraDuration = cameraPayload?.Duration ?? 0.12f;
            float cameraPositionAmplitude =
                cameraPayload?.PositionAmplitude ?? 0.04f;
            float cameraRotationAmplitude =
                cameraPayload?.RotationAmplitude ?? 0.6f;
            float cameraFrequency = cameraPayload?.Frequency ?? 30f;
            AnimationCurve cameraEnvelope = cameraPayload?.Envelope
                ?? new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(0.35f, 0.45f),
                    new Keyframe(1f, 0f));
            Vector3 shotPosition = cameraPayload?.ShotPosition
                ?? new Vector3(0f, 1.5f, -3.5f);
            Vector3 shotEulerAngles = cameraPayload?.ShotEulerAngles
                ?? Vector3.zero;
            float shotFieldOfView = cameraPayload?.ShotFieldOfView ?? 60f;
            Vector3 shotEndPosition = cameraPayload?.ShotEndPosition
                ?? new Vector3(0f, 1.5f, -3.5f);
            Vector3 shotEndEulerAngles = cameraPayload?.ShotEndEulerAngles
                ?? Vector3.zero;
            float shotEndFieldOfView =
                cameraPayload?.ShotEndFieldOfView ?? 60f;
            float shotBlendIn = cameraPayload?.ShotBlendIn ?? 0.08f;
            float shotMoveDuration =
                cameraPayload?.ShotMoveDuration ?? 0.2f;
            float shotHold = cameraPayload?.ShotHold ?? 0.08f;
            float shotBlendOut = cameraPayload?.ShotBlendOut ?? 0.2f;
            bool shotReturnBehindTarget =
                cameraPayload?.ShotReturnBehindTarget ?? true;
            AnimationCurve shotBlendCurve = cameraPayload?.ShotBlendCurve
                ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            AnimationCurve shotMoveCurve = cameraPayload?.ShotMoveCurve
                ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            if (type == NotifyType.Camera)
            {
                cameraMode = (CameraNotifyMode)EditorGUILayout.EnumPopup(
                    "Camera Mode", cameraMode);
                if (cameraMode == CameraNotifyMode.Shake)
                {
                    cameraDuration = DurationFrameField(
                        "Duration (f)", "카메라 셰이크 지속 프레임",
                        tc, cameraDuration);
                    cameraPositionAmplitude = EditorGUILayout.FloatField(
                        new GUIContent("Position Amplitude", "카메라 위치 흔들림 크기(월드 단위)"),
                        cameraPositionAmplitude);
                    cameraRotationAmplitude = EditorGUILayout.FloatField(
                        new GUIContent("Rotation Amplitude", "카메라 회전 흔들림 크기(도)"),
                        cameraRotationAmplitude);
                    cameraFrequency = EditorGUILayout.FloatField(
                        new GUIContent("Frequency", "초당 노이즈 진행 속도"),
                        cameraFrequency);
                    cameraEnvelope = EditorGUILayout.CurveField(
                        new GUIContent("Envelope", "셰이크 세기가 시간에 따라 줄어드는 형태"),
                        cameraEnvelope);
                }
                else
                {
                    shotBlendIn = Mathf.Max(0f, EditorGUILayout.FloatField(
                        new GUIContent("Blend In (s)",
                            "현재 TPS 카메라에서 Start 포즈까지 전환하는 시간입니다."),
                        shotBlendIn));
                    shotMoveDuration = Mathf.Max(0f, EditorGUILayout.FloatField(
                        new GUIContent("Move Duration (s)",
                            "Start 포즈에서 End 포즈까지 이동하는 시간입니다."),
                        shotMoveDuration));
                    shotHold = Mathf.Max(0f, EditorGUILayout.FloatField(
                        new GUIContent("End Hold (s)",
                            "End 포즈에서 정지해 있는 시간입니다."),
                        shotHold));
                    shotBlendOut = Mathf.Max(0f, EditorGUILayout.FloatField(
                        new GUIContent("Blend Out (s)",
                            "End 포즈에서 TPS 카메라로 복귀하는 시간입니다. 클립 길이와 무관하게 계속됩니다."),
                        shotBlendOut));
                    shotReturnBehindTarget = EditorGUILayout.Toggle(
                        new GUIContent("Return Behind Target",
                            "Blend Out 시작 시 복귀 목표를 현재 캐릭터 뒤쪽 TPS 구도로 맞춥니다."),
                        shotReturnBehindTarget);
                    shotFieldOfView = EditorGUILayout.Slider(
                        "Start FOV", shotFieldOfView, 1f, 179f);
                    shotEndFieldOfView = EditorGUILayout.Slider(
                        "End FOV", shotEndFieldOfView, 1f, 179f);
                    shotMoveCurve = EditorGUILayout.CurveField(
                        "Move Curve", shotMoveCurve);
                    shotBlendCurve = EditorGUILayout.CurveField(
                        "Blend Curve", shotBlendCurve);
                }
            }

            HitData hit = notify.Hit != null ? new HitData(notify.Hit) : null;
            HitNotifyAction hitAction = notify.Payload is HitNotifyPayload actionPayload
                ? actionPayload.Action
                : HitNotifyAction.Damage;
            float warningDuration = notify.Payload is HitNotifyPayload warningPayload
                ? warningPayload.WarningDuration
                : 0.3f;
            bool syncHitFromHitNotify = notify.Payload is HitNotifyPayload currentHit
                && currentHit.SyncWithEffect;
            if (type == NotifyType.Hit)
            {
                hit ??= new HitData();
                hitAction = (HitNotifyAction)EditorGUILayout.EnumPopup(
                    new GUIContent("Action",
                        "Damage는 피격을 적용하고, Parry Warning은 오버랩 범위 안의 플레이어에게만 패링 예고를 전달합니다."),
                    hitAction);
                if (hitAction == HitNotifyAction.ParryWarning)
                {
                    warningDuration = EditorGUILayout.FloatField(
                        new GUIContent("Warning Duration",
                            "예고를 받은 플레이어의 퍼펙트 회피 인정 시간(초)입니다."),
                        warningDuration);
                    syncHitFromHitNotify = false;
                }
                else
                {
                    syncHitFromHitNotify = EditorGUILayout.Toggle(
                        new GUIContent("Sync With Effect",
                            "Effect Key의 실제 실행 인스턴스에 Hit을 붙입니다. Effect가 정지되거나 풀에 반납될 때 Hit도 종료됩니다."),
                        syncHitFromHitNotify);
                }
                DrawHitData(hit, syncHitFromHitNotify,
                    hitAction == HitNotifyAction.Damage);
                if (syncHitFromHitNotify && string.IsNullOrEmpty(hit.EffectKey))
                    EditorGUILayout.HelpBox(
                        "Effect Key를 지정하고 Composite Effect Entry의 Binding Key와 동일하게 맞춰야 합니다.",
                        MessageType.Warning);
            }
            else if (type == NotifyType.Effect)
            {
                bool syncHitWithEffect = EditorGUILayout.Toggle(
                    new GUIContent("Sync Hit With Effect",
                        "Hit을 선택한 Effect Entry의 실제 생명주기에 묶습니다. Effect가 다른 섹션으로 Carry되면 Hit도 함께 유지되고, EffectHandle이 정지되거나 풀에 반납될 때 Hit도 종료됩니다."),
                    hit != null);
                if (syncHitWithEffect)
                {
                    if (hit == null)
                    {
                        hit = new HitData { Origin = HitOrigin.Effect };
                    }
                    DrawHitData(hit, true);
                    if (string.IsNullOrEmpty(hit.EffectKey))
                        EditorGUILayout.HelpBox(
                            "Effect Key를 지정하고 Composite Effect Entry의 Binding Key와 동일하게 맞춰야 합니다.",
                            MessageType.Warning);
                }
                else hit = null;
            }
            else hit = null;
            float endT = notify.EndNormalizedTime;
            EffectTransitionMode transitionMode = notify.TransitionMode;
            string nextSection = notify.NextSection;
            if (type == NotifyType.Hit)
            {
                if (syncHitFromHitNotify)
                {
                    EditorGUILayout.LabelField(
                        "Hit Lifetime", "Effect 생명주기 사용",
                        EditorStyles.miniLabel);
                }
                else
                {
                    endT = FrameField("Hit End (f)",
                        "Time 이하이면 단발, Time보다 크면 동적 범위 또는 반복 판정 구간입니다.",
                        tc, notify.EndNormalizedTime);
                }
            }
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
                        "Keep: Notify에서 재생 후 자연 소멸. Stop: Notify에서 재생 후 현재 상태 이탈 시 정지하며, Carry Section 지정 시 그 섹션 이탈까지 유지. Next: 지정한 다음 섹션으로 실제 전환될 때만 재생하고 그 섹션 이탈 시 정지."),
                    notify.TransitionMode);
                if (transitionMode == EffectTransitionMode.Stop
                    || transitionMode == EffectTransitionMode.Next)
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
                    bool carriesFromCurrent = transitionMode == EffectTransitionMode.Stop;
                    selectedSection = EditorGUILayout.Popup(
                        new GUIContent(carriesFromCurrent ? "Carry Section" : "Next Section",
                            carriesFromCurrent
                                ? "비워두면 현재 섹션 이탈 시 정지합니다. 지정하면 해당 섹션까지 유지하고 그 섹션 이탈 시 정지합니다."
                                : "실제 전환 목적지가 이 섹션일 때만 다음 섹션에서 이펙트를 생성합니다."),
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
                if (notify.Payload is CameraNotifyPayload editedCamera)
                {
                    editedCamera.Mode = cameraMode;
                    editedCamera.Duration = cameraDuration;
                    editedCamera.PositionAmplitude = cameraPositionAmplitude;
                    editedCamera.RotationAmplitude = cameraRotationAmplitude;
                    editedCamera.Frequency = cameraFrequency;
                    editedCamera.Envelope = cameraEnvelope;
                    editedCamera.ShotPosition = shotPosition;
                    editedCamera.ShotEulerAngles = shotEulerAngles;
                    editedCamera.ShotFieldOfView = shotFieldOfView;
                    editedCamera.ShotEndPosition = shotEndPosition;
                    editedCamera.ShotEndEulerAngles = shotEndEulerAngles;
                    editedCamera.ShotEndFieldOfView = shotEndFieldOfView;
                    editedCamera.ShotBlendIn = shotBlendIn;
                    editedCamera.ShotMoveDuration = shotMoveDuration;
                    editedCamera.ShotHold = shotHold;
                    editedCamera.ShotBlendOut = shotBlendOut;
                    editedCamera.ShotReturnBehindTarget =
                        shotReturnBehindTarget;
                    editedCamera.ShotBlendCurve = shotBlendCurve;
                    editedCamera.ShotMoveCurve = shotMoveCurve;
                }
                notify.Hit = hit;
                if (notify.Payload is HitNotifyPayload editedHit)
                {
                    editedHit.SyncWithEffect = syncHitFromHitNotify;
                    editedHit.Action = hitAction;
                    editedHit.WarningDuration = warningDuration;
                }
                notify.EndNormalizedTime = endT; notify.Locked = locked;
                notify.TransitionMode = transitionMode;
                notify.NextSection = nextSection;
                EditorUtility.SetDirty(_config);
                SceneView.RepaintAll();
                bool bindingChanged = (type == NotifyType.Hit
                        || type == NotifyType.Effect) && hit != null
                    && (hit.Origin != previousHitOrigin
                        || !string.Equals(hit.EffectKey, previousEffectKey,
                            StringComparison.Ordinal));
                if (typeChanged || bindingChanged) _fxDirty = true;
            }

            // Effect 타입이면 조합(Composite) 편집 + 씬 프리뷰를 여기서 인라인으로 (별도 탭 없음)
            if (notify.Payload is CameraNotifyPayload shotPayload
                && shotPayload.Mode == CameraNotifyMode.Shot)
                DrawCameraShotTools(shotPayload);

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

        private void DrawCameraShotTools(CameraNotifyPayload payload)
        {
            EditorGUILayout.Space(3f);
            if (_target == null)
            {
                EditorGUILayout.HelpBox(
                    "Target을 지정하면 Scene View 구도를 캡처할 수 있습니다.",
                    MessageType.Info);
                return;
            }

            _editCameraShotEndPose = GUILayout.Toolbar(
                _editCameraShotEndPose ? 1 : 0,
                new[] { "Edit Start Pose", "Edit End Pose" }) == 1;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Capture Start"))
                CaptureCameraShotFromSceneView(payload, false);
            if (GUILayout.Button("View Start"))
                MoveSceneViewToCameraShot(payload, false);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Capture End"))
                CaptureCameraShotFromSceneView(payload, true);
            if (GUILayout.Button("View End"))
                MoveSceneViewToCameraShot(payload, true);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Start와 End 구도를 각각 캡처하세요. Scene View의 파란 Start와 "
                + "청록 End 카메라를 확인하고, 선택한 포즈의 핸들로 직접 수정할 수 있습니다.",
                MessageType.None);
        }

        private void CaptureCameraShotFromSceneView(
            CameraNotifyPayload payload, bool endPose)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null || _target == null)
                return;

            Transform anchor = _target.transform;
            Transform sceneCamera = sceneView.camera.transform;
            Undo.RecordObject(_config, "Capture Camera Shot");
            Vector3 localPosition =
                anchor.InverseTransformPoint(sceneCamera.position);
            Vector3 localEulerAngles =
                (Quaternion.Inverse(anchor.rotation) * sceneCamera.rotation)
                .eulerAngles;
            if (endPose)
            {
                payload.ShotEndPosition = localPosition;
                payload.ShotEndEulerAngles = localEulerAngles;
                payload.ShotEndFieldOfView = sceneView.camera.fieldOfView;
            }
            else
            {
                payload.ShotPosition = localPosition;
                payload.ShotEulerAngles = localEulerAngles;
                payload.ShotFieldOfView = sceneView.camera.fieldOfView;
            }

            _editCameraShotEndPose = endPose;
            MarkCameraShotChanged();
        }

        private void MoveSceneViewToCameraShot(
            CameraNotifyPayload payload, bool endPose)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || _target == null) return;

            Transform anchor = _target.transform;
            Vector3 localPosition = endPose
                ? payload.ShotEndPosition
                : payload.ShotPosition;
            Vector3 localEulerAngles = endPose
                ? payload.ShotEndEulerAngles
                : payload.ShotEulerAngles;
            float fieldOfView = endPose
                ? payload.ShotEndFieldOfView
                : payload.ShotFieldOfView;
            Vector3 position = anchor.TransformPoint(localPosition);
            Quaternion rotation = anchor.rotation
                * Quaternion.Euler(localEulerAngles);
            MoveSceneViewToCameraPose(
                sceneView, position, rotation, fieldOfView);
            _editCameraShotEndPose = endPose;
        }

        private static void MoveSceneViewToCameraPose(SceneView sceneView,
            Vector3 position, Quaternion rotation, float fieldOfView)
        {
            sceneView.cameraSettings.fieldOfView = fieldOfView;
            float distance = Mathf.Max(0.1f, sceneView.cameraDistance);
            Vector3 pivot = position + rotation * Vector3.forward * distance;
            sceneView.LookAtDirect(pivot, rotation, sceneView.size);
            sceneView.Repaint();
        }

        private void MarkCameraShotChanged()
        {
            EditorUtility.SetDirty(_config);
            Repaint();
            SceneView.RepaintAll();
        }

        private void DrawHitData(HitData hit, bool effectOriginOnly = false,
            bool showDamage = true)
        {
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Hit Payload", EditorStyles.boldLabel);
            hit.ShowGizmo = EditorGUILayout.Toggle(
                new GUIContent("Show Gizmo",
                    "Config Tool 상단 Hit Gizmos가 켜져 있을 때 이 Hit의 판정 기즈모를 표시합니다."),
                hit.ShowGizmo);

            if (showDamage)
                hit.Damage = EditorGUILayout.FloatField("Damage", hit.Damage);
            hit.Strength = (AttackStrength)EditorGUILayout.EnumPopup(
                "Strength", hit.Strength);
            hit.TargetMask = EditorGUILayout.MaskField(
                "Target Mask", hit.TargetMask.value, GetLayerNames());
            hit.IncludeTriggers = EditorGUILayout.Toggle(
                "Include Triggers", hit.IncludeTriggers);

            if (effectOriginOnly) hit.Origin = HitOrigin.Effect;
            using (new EditorGUI.DisabledScope(effectOriginOnly))
                hit.Origin = (HitOrigin)EditorGUILayout.EnumPopup("Origin", hit.Origin);
            if (hit.Origin == HitOrigin.CharacterRoot
                || hit.Origin == HitOrigin.Socket)
            {
                bool socketOrigin = hit.Origin == HitOrigin.Socket;
                if (socketOrigin)
                    hit.Socket = EditorGUILayout.TextField("Socket", hit.Socket);
                string[] trackingLabels = socketOrigin
                    ? new[] { "Follow Socket", "Keep World Pose" }
                    : new[] { "Follow Root", "Keep World Pose" };
                hit.OriginTracking = (HitOriginTracking)EditorGUILayout.Popup(
                    new GUIContent(socketOrigin ? "Socket Tracking" : "Root Tracking",
                        socketOrigin
                            ? "Follow Socket은 판정이 소켓을 계속 따라갑니다. Keep World Pose는 Hit 시작 순간의 위치와 회전을 월드 좌표로 유지합니다."
                            : "Follow Root는 판정이 캐릭터 루트를 계속 따라갑니다. Keep World Pose는 Hit 시작 순간의 위치와 회전을 월드 좌표로 유지합니다."),
                    (int)hit.OriginTracking, trackingLabels);
            }
            else if (hit.Origin == HitOrigin.Effect)
                hit.EffectKey = DrawEffectBindingKey(hit.EffectKey);
            hit.PositionOffset = EditorGUILayout.Vector3Field(
                "Position Offset", hit.PositionOffset);
            hit.EulerOffset = EditorGUILayout.Vector3Field(
                "Euler Offset", hit.EulerOffset);

            hit.Shape = (HitShape)EditorGUILayout.EnumPopup("Shape", hit.Shape);
            switch (hit.Shape)
            {
                case HitShape.Sphere:
                    hit.Radius = EditorGUILayout.FloatField("Radius", hit.Radius);
                    break;
                case HitShape.Cone:
                    hit.Radius = EditorGUILayout.FloatField("Radius", hit.Radius);
                    hit.Angle = EditorGUILayout.Slider("Angle", hit.Angle, 0f, 360f);
                    break;
                case HitShape.Box:
                    hit.BoxSize = EditorGUILayout.Vector3Field("Box Size", hit.BoxSize);
                    break;
                case HitShape.Capsule:
                    hit.Radius = EditorGUILayout.FloatField("Radius", hit.Radius);
                    hit.Length = EditorGUILayout.FloatField("Length", hit.Length);
                    break;
                case HitShape.ExpandingSphere:
                    hit.StartRadius = EditorGUILayout.FloatField(
                        "Start Radius", hit.StartRadius);
                    hit.EndRadius = EditorGUILayout.FloatField(
                        "End Radius", hit.EndRadius);
                    hit.Duration = EditorGUILayout.FloatField("Duration", hit.Duration);
                    hit.RadiusCurve = EditorGUILayout.CurveField(
                        "Radius Curve", hit.RadiusCurve);
                    break;
                case HitShape.ExpandingCone:
                    hit.StartRadius = EditorGUILayout.FloatField(
                        "Start Range", hit.StartRadius);
                    hit.EndRadius = EditorGUILayout.FloatField(
                        "End Range", hit.EndRadius);
                    hit.Angle = EditorGUILayout.Slider("Angle", hit.Angle, 0f, 360f);
                    hit.Duration = EditorGUILayout.FloatField("Duration", hit.Duration);
                    hit.RadiusCurve = EditorGUILayout.CurveField(
                        "Range Curve", hit.RadiusCurve);
                    break;
            }

            hit.QueryMode = (HitQueryMode)EditorGUILayout.EnumPopup(
                new GUIContent("Query Mode",
                    "Overlap은 현재 위치만 검사합니다. Sweep은 구간형 Hit에서 이전 위치부터 현재 위치까지 함께 검사합니다."),
                hit.QueryMode);
            hit.Frequency = (HitFrequency)EditorGUILayout.EnumPopup(
                "Frequency", hit.Frequency);
            if (hit.Frequency == HitFrequency.RepeatInterval)
                hit.RepeatInterval = EditorGUILayout.FloatField(
                    "Repeat Interval", hit.RepeatInterval);
        }

        private string DrawEffectBindingKey(string currentKey)
        {
            var keys = new List<string>();
            if (_config != null)
            {
                for (int clipIndex = 0; clipIndex < _config.Clips.Count; clipIndex++)
                {
                    List<TrackNotify> notifies = _config.Clips[clipIndex].Notifies;
                    for (int notifyIndex = 0; notifyIndex < notifies.Count; notifyIndex++)
                    {
                        CompositeEffect effect = notifies[notifyIndex].Effect;
                        if (effect == null) continue;
                        for (int entryIndex = 0; entryIndex < effect.Entries.Count; entryIndex++)
                        {
                            string key = effect.Entries[entryIndex]?.BindingKey?.Trim();
                            if (!string.IsNullOrEmpty(key) && !keys.Contains(key))
                                keys.Add(key);
                        }
                    }
                }
            }

            keys.Sort(StringComparer.Ordinal);
            currentKey = currentKey?.Trim() ?? "";
            bool missing = !string.IsNullOrEmpty(currentKey) && !keys.Contains(currentKey);
            if (missing) keys.Add(currentKey);

            var labels = new string[keys.Count + 1];
            labels[0] = "(Select Effect Key)";
            for (int i = 0; i < keys.Count; i++)
                labels[i + 1] = missing && keys[i] == currentKey
                    ? $"{keys[i]} (Missing)"
                    : keys[i];

            int selected = string.IsNullOrEmpty(currentKey)
                ? 0
                : Mathf.Max(0, keys.IndexOf(currentKey) + 1);
            selected = EditorGUILayout.Popup("Effect Key", selected, labels);
            string result = selected > 0 ? keys[selected - 1] : "";

            if (keys.Count == 0)
                EditorGUILayout.HelpBox(
                    "먼저 Effect Notify의 CompositeEffect Entry에 Binding Key를 지정하세요.",
                    MessageType.Warning);
            else if (missing && result == currentKey)
                EditorGUILayout.HelpBox(
                    $"'{currentKey}' 키를 가진 Effect Entry가 현재 Config에 없습니다.",
                    MessageType.Warning);
            return result;
        }

        private static string[] s_layerNames;

        private static string[] GetLayerNames()
        {
            if (s_layerNames == null) s_layerNames = BuildLayerNames();
            return s_layerNames;
        }

        private static string[] BuildLayerNames()
        {
            var names = new string[32];
            for (int i = 0; i < names.Length; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                names[i] = string.IsNullOrEmpty(layerName) ? $"Layer {i}" : layerName;
            }
            return names;
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
                    NotifyType.Effect => "E", NotifyType.Hit => "H", NotifyType.Camera => "C",
                    NotifyType.Sound  => "S", _ => "N",
                };
                int f = Mathf.RoundToInt(n.NormalizedTime * clipFrames);
                string label = (n.Locked ? "·" : "") + $"{head}{f}";

                bool cur = idx == _selectedNotify && _notifyClipIdx == _selectedClip;
                Color prev = GUI.backgroundColor;
                if (cur) GUI.backgroundColor = new Color(0.95f, 0.85f, 0.25f);
                Rect notifyButtonRect = GUILayoutUtility.GetRect(
                    48f, EditorGUIUtility.singleLineHeight,
                    EditorStyles.miniButton, GUILayout.Width(48f));
                Event currentEvent = Event.current;
                bool contextOpened = currentEvent.type == EventType.MouseDown
                    && currentEvent.button == 1
                    && notifyButtonRect.Contains(currentEvent.mousePosition);
                if (contextOpened)
                {
                    ShowNotifyButtonMenu(tc, n);
                    currentEvent.Use();
                }
                else if (GUI.Button(notifyButtonRect, label, EditorStyles.miniButton))
                {
                    _selectedNotify = idx; _notifyClipIdx = _selectedClip;
                    GUI.FocusControl(null);
                    Repaint();
                }
                GUI.backgroundColor = prev;

                if (p % perRow == perRow - 1 || p == order.Count - 1) EditorGUILayout.EndHorizontal();
            }
        }

        private void ShowNotifyButtonMenu(TrackClip clip, TrackNotify notify)
        {
            int clipIndex = _config.Clips.IndexOf(clip);
            float pasteTime = notify.NormalizedTime;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy Notify"), false, () =>
            {
                _notifyClipboard = CloneNotify(notify);
            });
            if (_notifyClipboard != null)
            {
                menu.AddItem(
                    new GUIContent($"Paste {_notifyClipboard.Type} Notify Here"),
                    false, () => PasteNotify(clipIndex, pasteTime));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste Notify Here"));
            }
            menu.ShowAsContext();
        }

        private static readonly Color HitColor =
            new Color(1f, 0.25f, 0.12f, 0.95f);
        private static readonly Color HitRangeColor =
            new Color(1f, 0.65f, 0.1f, 0.35f);

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_target == null || _config == null) return;
            if (_notifyClipIdx < 0 || _notifyClipIdx >= _config.Clips.Count) return;

            TrackClip clip = _config.Clips[_notifyClipIdx];
            if (_selectedNotify < 0 || _selectedNotify >= clip.Notifies.Count) return;

            TrackNotify notify = clip.Notifies[_selectedNotify];
            if (notify?.Payload is CameraNotifyPayload cameraPayload
                && cameraPayload.Mode == CameraNotifyMode.Shot)
                DrawCameraShotHandles(cameraPayload);

            if (!_showHitPreviewGizmos) return;
            HitData hit = notify?.Payload is HitNotifyPayload
                || notify?.Payload is EffectNotifyPayload
                ? notify.Hit
                : null;
            if (hit == null || !hit.ShowGizmo) return;

            Transform origin = ResolveHitPreviewOrigin(notify);
            if (origin == null) return;

            Vector3 center = origin.TransformPoint(hit.PositionOffset);
            Quaternion rotation = origin.rotation * hit.RotationOffset;
            DrawHitShape(hit, center, rotation, HitPreviewProgress(clip, notify));
            DrawHitLabel(hit, center);
            DrawHitHandles(hit, origin, center, rotation);
        }

        private void DrawCameraShotHandles(CameraNotifyPayload payload)
        {
            Transform anchor = _target.transform;
            Vector3 startPosition = anchor.TransformPoint(payload.ShotPosition);
            Quaternion startRotation = anchor.rotation
                * Quaternion.Euler(payload.ShotEulerAngles);
            Vector3 endPosition = anchor.TransformPoint(payload.ShotEndPosition);
            Quaternion endRotation = anchor.rotation
                * Quaternion.Euler(payload.ShotEndEulerAngles);

            DrawCameraShotMarker(
                startPosition, startRotation,
                new Color(0.2f, 0.55f, 1f), "Shot Start");
            DrawCameraShotMarker(
                endPosition, endRotation,
                new Color(0.1f, 1f, 0.85f), "Shot End");
            using (new Handles.DrawingScope(new Color(0.2f, 0.85f, 1f, 0.8f)))
                Handles.DrawDottedLine(startPosition, endPosition, 4f);

            Vector3 position = _editCameraShotEndPose
                ? endPosition
                : startPosition;
            Quaternion rotation = _editCameraShotEndPose
                ? endRotation
                : startRotation;

            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(position, rotation);
            Quaternion newRotation = Handles.RotationHandle(rotation, position);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(_config, "Edit Camera Shot Pose");
            Vector3 localPosition = anchor.InverseTransformPoint(newPosition);
            Vector3 localEulerAngles =
                (Quaternion.Inverse(anchor.rotation) * newRotation).eulerAngles;
            if (_editCameraShotEndPose)
            {
                payload.ShotEndPosition = localPosition;
                payload.ShotEndEulerAngles = localEulerAngles;
            }
            else
            {
                payload.ShotPosition = localPosition;
                payload.ShotEulerAngles = localEulerAngles;
            }
            MarkCameraShotChanged();
        }

        private static void DrawCameraShotMarker(Vector3 position,
            Quaternion rotation, Color color, string label)
        {
            float size = HandleUtility.GetHandleSize(position) * 0.15f;

            using (new Handles.DrawingScope(
                color,
                Matrix4x4.TRS(position, rotation, Vector3.one)))
            {
                Handles.DrawWireCube(
                    Vector3.zero, new Vector3(size * 1.6f, size, size * 0.8f));
                Handles.DrawLine(
                    Vector3.zero, Vector3.forward * size * 2.5f);
            }
            Handles.Label(position + Vector3.up * size, label);
        }

        private Transform ResolveHitPreviewOrigin(TrackNotify notify)
        {
            if (notify.Hit.Origin == HitOrigin.Effect)
            {
                for (int i = 0; i < _fxAtoms.Count; i++)
                {
                    FxPreviewAtom atom = _fxAtoms[i];
                    if (!string.Equals(atom.Entry.BindingKey?.Trim(),
                            notify.Hit.EffectKey, StringComparison.Ordinal)
                        || atom.Root == null)
                        continue;
                    if (!atom.Root.activeInHierarchy) PlaceFxAtom(atom);
                    return atom.Root.transform;
                }
                return null;
            }

            if (notify.Hit.Origin != HitOrigin.Socket) return _target.transform;
            Transform socket = FindDescendant(_target.transform, notify.Hit.Socket);
            return socket != null ? socket : _target.transform;
        }

        private float HitPreviewProgress(TrackClip clip, TrackNotify notify)
        {
            if (clip.Clip == null || clip.Clip.length <= 0f) return 0f;

            float normalizedTime;
            if (_comboMode && _comboActiveClip == _notifyClipIdx)
            {
                normalizedTime = _comboClipTime / clip.Clip.length;
            }
            else
            {
                float clipDuration = clip.Clip.length / Mathf.Max(0.01f, clip.Speed);
                float localTime = _trackTime - GetClipStartTime(_notifyClipIdx);
                normalizedTime = clipDuration > 0f ? localTime / clipDuration : 0f;
            }

            if (notify.IsInterval)
                return Mathf.InverseLerp(
                    notify.NormalizedTime, notify.EndNormalizedTime, normalizedTime);

            if (notify.Payload is EffectNotifyPayload && notify.Hit != null)
            {
                float clipDuration = clip.Clip.length / Mathf.Max(0.01f, clip.Speed);
                float elapsed = Mathf.Max(
                    0f, (normalizedTime - notify.NormalizedTime) * clipDuration);
                return Mathf.Clamp01(elapsed / notify.Hit.Duration);
            }

            return 0f;
        }

        private static void DrawHitShape(
            HitData hit, Vector3 center, Quaternion rotation, float progress)
        {
            using (new Handles.DrawingScope(HitColor))
            {
                switch (hit.Shape)
                {
                    case HitShape.Sphere:
                        DrawWireSphere(center, rotation, hit.Radius);
                        break;
                    case HitShape.Cone:
                        DrawWireCone(center, rotation, hit.Radius, hit.Angle);
                        break;
                    case HitShape.Box:
                        using (new Handles.DrawingScope(
                            HitColor, Matrix4x4.TRS(center, rotation, Vector3.one)))
                            Handles.DrawWireCube(Vector3.zero, hit.BoxSize);
                        break;
                    case HitShape.Capsule:
                        DrawWireCapsule(
                            center, center + rotation * Vector3.forward * hit.Length,
                            rotation, hit.Radius);
                        break;
                    case HitShape.ExpandingSphere:
                        using (new Handles.DrawingScope(HitRangeColor))
                        {
                            DrawWireSphere(center, rotation, hit.StartRadius);
                            DrawWireSphere(center, rotation, hit.EndRadius);
                        }
                        DrawWireSphere(
                            center, rotation, Mathf.Max(0f, hit.EvaluateRadius(progress)));
                        break;
                    case HitShape.ExpandingCone:
                        using (new Handles.DrawingScope(HitRangeColor))
                        {
                            DrawWireCone(
                                center, rotation, hit.StartRadius, hit.Angle);
                            DrawWireCone(
                                center, rotation, hit.EndRadius, hit.Angle);
                        }
                        DrawWireCone(
                            center, rotation,
                            Mathf.Max(0f, hit.EvaluateRadius(progress)), hit.Angle);
                        break;
                }
            }
        }

        private static void DrawHitLabel(HitData hit, Vector3 center)
        {
            float size = HandleUtility.GetHandleSize(center);
            string detail = hit.Shape switch
            {
                HitShape.Sphere => $"r {hit.Radius:0.00}",
                HitShape.Cone => $"r {hit.Radius:0.00} / {hit.Angle:0.#} deg",
                HitShape.Box =>
                    $"{hit.BoxSize.x:0.00} x {hit.BoxSize.y:0.00} x {hit.BoxSize.z:0.00}",
                HitShape.Capsule => $"r {hit.Radius:0.00} / len {hit.Length:0.00}",
                HitShape.ExpandingSphere =>
                    $"{hit.StartRadius:0.00} -> {hit.EndRadius:0.00}",
                HitShape.ExpandingCone =>
                    $"{hit.StartRadius:0.00} -> {hit.EndRadius:0.00} / {hit.Angle:0.#} deg",
                _ => "",
            };
            Handles.Label(center + Vector3.up * size * 0.35f,
                $"{hit.Shape}  {detail}");
        }

        private void DrawHitHandles(
            HitData hit, Transform origin, Vector3 center, Quaternion rotation)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(center, rotation);
            Quaternion newRotation = Handles.RotationHandle(rotation, center);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Edit Hit Transform");
                hit.PositionOffset = origin.InverseTransformPoint(newCenter);
                hit.EulerOffset =
                    (Quaternion.Inverse(origin.rotation) * newRotation).eulerAngles;
                MarkHitPreviewChanged();
                center = newCenter;
                rotation = newRotation;
            }

            switch (hit.Shape)
            {
                case HitShape.Sphere:
                case HitShape.Cone:
                case HitShape.Capsule:
                    EditorGUI.BeginChangeCheck();
                    float radius = Handles.RadiusHandle(rotation, center, hit.Radius);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Edit Hit Radius");
                        hit.Radius = radius;
                        MarkHitPreviewChanged();
                    }
                    break;
                case HitShape.Box:
                    EditorGUI.BeginChangeCheck();
                    Vector3 size = Handles.ScaleHandle(
                        hit.BoxSize, center, rotation, HandleUtility.GetHandleSize(center));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Edit Hit Box");
                        hit.BoxSize = size;
                        MarkHitPreviewChanged();
                    }
                    break;
                case HitShape.ExpandingSphere:
                case HitShape.ExpandingCone:
                    using (new Handles.DrawingScope(new Color(1f, 0.75f, 0.1f, 1f)))
                    {
                        EditorGUI.BeginChangeCheck();
                        float startRadius = Handles.RadiusHandle(
                            rotation, center, hit.StartRadius);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(_config, "Edit Hit Start Radius");
                            hit.StartRadius = Mathf.Min(startRadius, hit.EndRadius);
                            MarkHitPreviewChanged();
                        }
                    }
                    using (new Handles.DrawingScope(HitColor))
                    {
                        EditorGUI.BeginChangeCheck();
                        float endRadius = Handles.RadiusHandle(
                            rotation, center, hit.EndRadius);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(_config, "Edit Hit End Radius");
                            hit.EndRadius = endRadius;
                            MarkHitPreviewChanged();
                        }
                    }
                    break;
            }
        }

        private void MarkHitPreviewChanged()
        {
            EditorUtility.SetDirty(_config);
            Repaint();
            SceneView.RepaintAll();
        }

        private static void DrawWireSphere(
            Vector3 center, Quaternion rotation, float radius)
        {
            if (radius <= 0f) return;
            Handles.DrawWireDisc(center, rotation * Vector3.right, radius);
            Handles.DrawWireDisc(center, rotation * Vector3.up, radius);
            Handles.DrawWireDisc(center, rotation * Vector3.forward, radius);
        }

        private static void DrawWireCone(
            Vector3 center, Quaternion rotation, float radius, float angle)
        {
            if (radius <= 0f) return;
            float halfAngle = Mathf.Clamp(angle * 0.5f, 0f, 180f);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 horizontalStart = Quaternion.AngleAxis(-halfAngle, up) * forward;
            Vector3 verticalStart = Quaternion.AngleAxis(-halfAngle, right) * forward;

            Handles.DrawWireArc(center, up, horizontalStart, angle, radius);
            Handles.DrawWireArc(center, right, verticalStart, angle, radius);

            float axial = Mathf.Cos(halfAngle * Mathf.Deg2Rad) * radius;
            float ringRadius = Mathf.Sin(halfAngle * Mathf.Deg2Rad) * radius;
            Vector3 capCenter = center + forward * axial;
            Handles.DrawWireDisc(capCenter, forward, ringRadius);
            Handles.DrawLine(center, capCenter + right * ringRadius);
            Handles.DrawLine(center, capCenter - right * ringRadius);
            Handles.DrawLine(center, capCenter + up * ringRadius);
            Handles.DrawLine(center, capCenter - up * ringRadius);
        }

        private static void DrawWireCapsule(
            Vector3 start, Vector3 end, Quaternion rotation, float radius)
        {
            if (radius <= 0f) return;
            Vector3 right = rotation * Vector3.right * radius;
            Vector3 up = rotation * Vector3.up * radius;
            DrawWireSphere(start, rotation, radius);
            DrawWireSphere(end, rotation, radius);
            Handles.DrawLine(start + right, end + right);
            Handles.DrawLine(start - right, end - right);
            Handles.DrawLine(start + up, end + up);
            Handles.DrawLine(start - up, end - up);
        }

        private static void DrawSeparator()
        {
            EditorGUILayout.Space(2);
            var r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.3f, 0.3f, 0.3f, 0.5f));
        }
    }

    internal static class NotifyPayloadMigration
    {
        [MenuItem("Tools/ZZZ/Migrate Animation Notify Payloads")]
        private static void MigrateAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:AnimationConfig");
            int configCount = 0;
            int notifyCount = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AnimationConfig config = AssetDatabase.LoadAssetAtPath<AnimationConfig>(path);
                if (config == null) continue;

                bool changed = false;
                for (int clipIndex = 0; clipIndex < config.Clips.Count; clipIndex++)
                {
                    List<TrackNotify> notifies = config.Clips[clipIndex].Notifies;
                    for (int notifyIndex = 0; notifyIndex < notifies.Count; notifyIndex++)
                    {
                        TrackNotify notify = notifies[notifyIndex];
                        if (notify == null || !notify.MigratePayload()) continue;
                        changed = true;
                        notifyCount++;
                    }
                }

                if (!changed) continue;
                EditorUtility.SetDirty(config);
                configCount++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Notify Payload migration complete: {configCount} configs, "
                + $"{notifyCount} notifies.");
        }
    }
}
