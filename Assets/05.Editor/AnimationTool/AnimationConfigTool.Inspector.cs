using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ;
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
            EditorGUILayout.LabelField($"Modules  ({tc.Modules.Count})", EditorStyles.boldLabel);

            int removeAt = -1;
            for (int i = 0; i < tc.Modules.Count; i++)
            {
                var m = tc.Modules[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(m != null ? m.DisplayName : "(null)", GUILayout.Width(170));
                if (GUILayout.Button("−", GUILayout.Width(24))) removeAt = i;
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                if (m is WindowModule wm)   // 윈도우 모듈(무적/패링/…) 공용 — Start/End 슬라이더
                {
                    float s = wm.Start, e = wm.End;
                    FrameWindowField("   Window (f)", "이 모듈이 작동하는 프레임 구간", tc, ref s, ref e);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Edit Module");
                        wm.Start = Mathf.Clamp01(Mathf.Min(s, e));
                        wm.End   = Mathf.Clamp01(Mathf.Max(s, e));
                        EditorUtility.SetDirty(_config);
                    }
                }
                else EditorGUI.EndChangeCheck();
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
            bool lockRot = EditorGUILayout.Toggle(
                new GUIContent("Lock Rotation", "이 클립 동안 이동 입력이 있어도 캐릭터 회전 금지 (피격/경직)"),
                tc.LockRotation);
            float lockWS = tc.LockWindowStart, lockWE = tc.LockWindowEnd;
            if (lockRot)
                FrameWindowField("  Lock Window (f)", "회전을 잠그는 프레임 구간. End<=Start면 섹션 전체",
                    tc, ref lockWS, ref lockWE);
            bool faceInput = tc.FaceInputOnEnter;   // 토글은 아래 고급 'Facing' 그룹에서 그림 (조준 옵션끼리 묶음)

            // 루프 전진 평속화 — RootMotion 루프 섹션 전용 (걷기/달리기). 비루프/비RootMotion엔 영향 없음.
            bool smoothLoop = tc.SmoothLoopSpeed;
            float backScale = tc.BackMotionScale;
            if (mode == MoveMode.RootMotion)
            {
                // Smooth Loop Speed는 루프 클립에서만 의미 있는 기능 → 루프일 때만 토글 노출(켜고/끄기).
                // 비루프 클립은 런타임에서 무시되므로 토글을 숨기고, 켜져 있던 stale 값은 꺼서 정리한다.
                if (tc.IsLooping)
                    smoothLoop = EditorGUILayout.Toggle(
                        new GUIContent("Smooth Loop Speed", "루프 클립 끝 정지프레임이 만드는 전진 '틱'을 한 루프 평균속도로 제거. 끄면 원본 보폭감(프레임별 가감속) 유지하되 틱이 보일 수 있음"),
                        tc.SmoothLoopSpeed);
                else
                    smoothLoop = false;   // 비루프 → 항상 끔 (토글 숨김)
                backScale = EditorGUILayout.FloatField(
                    new GUIContent("Back Motion Scale", "후진(캐릭터 기준 -Z) 루트모션만 이 배율로 증폭. 전진/측면 불변. 1=원본, 1.2=뒤로 빠지는 모션 20% 강조(스텝백·recoil 강조)"),
                    tc.BackMotionScale);
            }

            // ── 고급 (접기) — Boost / Target Tracking / Section Turn ──
            // 값은 항상 현재값으로 초기화 → 접혀서 UI를 안 그려도 저장 로직이 그대로 유지됨
            float boostSpd = tc.StartBoostSpeed,  boostT = tc.StartBoostTime;
            bool  track    = tc.EnableTracking,   face   = tc.FaceTarget;
            float faceTurn = tc.FaceTurnSpeed;
            float fwS = tc.FaceWindowStart, fwE = tc.FaceWindowEnd;
            float twS = tc.TrackWindowStart, twE = tc.TrackWindowEnd, stopD = tc.StopDistance;
            bool  secTurn  = tc.SectionTurn;
            float turnWS   = tc.TurnWindowStart, turnWE = tc.TurnWindowEnd;

            // 접혀 있어도 어떤 고급 옵션이 켜져 있는지 라벨로 표시
            string advLabel = "고급";
            if (boostSpd > 0f)          advLabel += " · Boost";
            if (track)                  advLabel += " · Warp";
            if (faceInput)              advLabel += " · FaceInput";
            if (face)                   advLabel += " · FaceTarget";
            if (secTurn)                advLabel += " · Turn";
            _clipAdvFold = EditorGUILayout.Foldout(_clipAdvFold, advLabel, true);
            if (_clipAdvFold)
            {
                boostSpd = EditorGUILayout.FloatField(
                    new GUIContent("Start Boost", "클립 시작 순간 진행 방향 속도 (0=끔). 시간이 지나며 감쇠"),
                    tc.StartBoostSpeed);
                if (boostSpd > 0f)
                    boostT = EditorGUILayout.FloatField("  Boost Time(s)", tc.StartBoostTime);

                if (mode == MoveMode.RootMotion)
                {
                    EditorGUILayout.LabelField("── Facing & Warp ──", EditorStyles.miniBoldLabel);

                    // [이동 워프] 루트모션 이동을 적 방향으로 끌어와 StopDistance 앞에서 멈춤 (회전과 별개)
                    track = EditorGUILayout.Toggle(
                        new GUIContent("Target Warp (Move)", "전방 적 방향으로 루트모션 이동을 끌어와 StopDistance 앞에서 멈춤. 회전과 독립"),
                        tc.EnableTracking);
                    if (track)
                    {
                        FrameWindowField("  Warp Window (f)", "이동 워프가 작동하는 프레임 구간. 타격 이후엔 끊을 것",
                            tc, ref twS, ref twE);
                        stopD = EditorGUILayout.FloatField(
                            new GUIContent("  Stop Distance", "타겟 앞 정지 거리 (관통 방지) — 이동 워프 전용"), tc.StopDistance);
                    }

                    // [입력 조준] 진입 순간 내 이동 입력(WASD) 방향으로 1회 스냅. 적 방향(Face Target)보다 우선.
                    faceInput = EditorGUILayout.Toggle(
                        new GUIContent("Face Input (Enter)", "진입 순간 이동 입력 방향으로 즉시 스냅 (적이 아니라 내 입력 방향). 입력 있으면 Face Target보다 우선"),
                        tc.FaceInputOnEnter);

                    // [타겟 조준] Snap+Lock-on 통합 — FaceWindow 동안 타겟 향해 회전. [0,0]=진입 스냅, 넓히면 락온
                    face = EditorGUILayout.Toggle(
                        new GUIContent("Face Target", "FaceWindow 동안 타겟을 향해 회전. Window [0,0]=진입 1회 스냅 / 넓히면 락온(적이 움직여도 따라붙음). Target Warp와 독립"),
                        tc.FaceTarget);
                    if (face)
                    {
                        FrameWindowField("  Face Window (f)", "조준 작동 구간. Start==End면 진입 1회 스냅, 넓히면 그 구간 내내 락온",
                            tc, ref fwS, ref fwE);
                        faceTurn = EditorGUILayout.FloatField(
                            new GUIContent("  Turn Speed(°/s)", "조준 회전 각속도. 0=즉시(스냅)"), tc.FaceTurnSpeed);
                    }
                }

                if (mode == MoveMode.RootMotion)
                {
                    secTurn = EditorGUILayout.Toggle(
                        new GUIContent("Section Turn (Root)", "클립에 구워진 턴 회전을 Bip001 yaw 델타로 추출해 transform에 적용. 각도는 애니가 결정. 켤 때 Lock Rotation도 함께 둘 것"),
                        tc.SectionTurn);
                    if (secTurn)
                        FrameWindowField("  Turn Window (f)", "yaw 델타를 추출하는 프레임 구간. End<=Start면 섹션 전체. 구간 밖에선 추출만 멈추고 회전은 유지",
                            tc, ref turnWS, ref turnWE);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Edit Clip");
                // 섹션 이름이 비었으면 클립 이름으로 자동 채움
                if (string.IsNullOrEmpty(sect) && clip != null) sect = clip.name;
                tc.SectionName = sect;
                tc.Clip = clip;
                tc.Speed = Mathf.Max(0.01f, spd);
                tc.MoveMode = mode;
                tc.LockRotation = lockRot;
                tc.LockWindowStart = Mathf.Clamp01(Mathf.Min(lockWS, lockWE));
                tc.LockWindowEnd   = Mathf.Clamp01(Mathf.Max(lockWS, lockWE));
                tc.FaceInputOnEnter = faceInput;
                tc.SmoothLoopSpeed = smoothLoop;
                tc.BackMotionScale = Mathf.Max(0f, backScale);
                tc.StartBoostSpeed = Mathf.Max(0f, boostSpd);
                tc.StartBoostTime  = Mathf.Max(0.01f, boostT);
                tc.EnableTracking   = track;
                tc.TrackWindowStart = Mathf.Clamp01(Mathf.Min(twS, twE));
                tc.TrackWindowEnd   = Mathf.Clamp01(Mathf.Max(twS, twE));
                tc.StopDistance     = Mathf.Max(0f, stopD);
                tc.FaceTarget       = face;
                tc.FaceWindowStart  = Mathf.Clamp01(Mathf.Min(fwS, fwE));
                tc.FaceWindowEnd    = Mathf.Clamp01(Mathf.Max(fwS, fwE));
                tc.FaceTurnSpeed    = Mathf.Max(0f, faceTurn);
                tc.SectionTurn     = secTurn;
                tc.TurnWindowStart = Mathf.Clamp01(Mathf.Min(turnWS, turnWE));
                tc.TurnWindowEnd   = Mathf.Clamp01(Mathf.Max(turnWS, turnWE));
                EditorUtility.SetDirty(_config);
            }

            DrawModules(tc);

            // Loop은 클립 임포트 설정(Loop Time)에서 자동 표시 — 편집 불가
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle(new GUIContent("Loop (클립 설정)",
                    "클립 임포트의 Loop Time 설정을 그대로 표시 (config에서 관리 안 함)"), tc.IsLooping);

            if (tc.Clip != null)
                EditorGUILayout.LabelField(
                    $"{tc.Clip.length:F3}s → {tc.Clip.length / Mathf.Max(0.01f, tc.Speed):F3}s   " +
                    $"({Mathf.RoundToInt(tc.Clip.length * tc.Clip.frameRate)}f)",
                    EditorStyles.miniLabel);

            // ── Links (다음 섹션 분기) ───────────────────────────
            DrawSeparator();
            EditorGUILayout.LabelField($"Links  ({tc.Links.Count})  —  헤더 클릭=펼치기+강조 / ▲▼=순서",
                EditorStyles.boldLabel);
            DrawLinksEditor(tc.Links, idx);

            DrawSeparator();
            EditorGUILayout.LabelField($"Notifies  ({tc.Notifies.Count})  —  우클릭 추가",
                EditorStyles.miniLabel);
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
                var chipIc = ReadInput(link);   // 표시 전용(비저장) — 미마이그레이션이면 레거시 합성
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
                    EditorGUI.BeginChangeCheck();

                    // ── 대상 ──
                    var targetCfg = (AnimationConfig)EditorGUILayout.ObjectField(
                        "Target Config", link.TargetConfig, typeof(AnimationConfig), false);
                    var      cfgForSections = targetCfg != null ? targetCfg : _config;
                    string[] sectionOptions = BuildSectionOptions(cfgForSections);
                    int curIdx = Mathf.Max(0, Array.IndexOf(sectionOptions,
                        string.IsNullOrEmpty(link.TargetSection) ? "(End/Entry)" : link.TargetSection));
                    int newIdx = EditorGUILayout.Popup("→ Section", curIdx, ShortAll(sectionOptions));

                    // 입력 조건 편집 — 시드는 InputCondition(미마이그레이션이면 레거시), 변경 시 EnsureInput에 기록.
                    var seedIc = ReadInput(link);
                    ComboInput seedAttack = seedIc?.Attack ?? ComboInput.None;
                    MoveDir    seedDir    = seedIc?.Direction ?? MoveDir.Any;
                    bool       seedHeld   = seedIc?.RequireHeld ?? false;

                    var attack = (ComboInput)ColoredEnum(new GUIContent("Attack"), k_chipAttack, seedAttack);
                    // 특정 공격 키일 때만 의미 — 그 키가 '눌려있는 동안' 조건 충족 (차지 루프용)
                    bool requireHeld = seedHeld;
                    if (attack != ComboInput.None && attack != ComboInput.Any)
                        requireHeld = EditorGUILayout.Toggle(
                            new GUIContent("  Require Held", "체크 시 이 키가 '지금 눌려있을(held) 때' 충족 (누름 버퍼 대신 홀드). OnEnd 자기-루프+EntryOffset과 함께 차지 루프"),
                            seedHeld);
                    var dir    = (MoveDir)ColoredEnum(new GUIContent("Direction"), k_chipDir, seedDir);
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
                        var ic = EnsureInput(link);
                        ic.Attack          = attack;
                        ic.RequireHeld     = requireHeld;
                        ic.Direction       = dir;
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

        // 에디터는 현재 입력 조건(InputCondition) 편집 UI만 제공 — link.Condition을 InputCondition으로 다룬다.
        // 없거나 다른 타입이면 새로 만들어 채운다(향후 몬스터 조건 타입 추가 시 타입 선택 UI로 확장).
        private static InputCondition EnsureInput(ClipLink link)
        {
            if (link.Condition is InputCondition ic) return ic;
            // 없거나 다른 타입이면 빈 입력 조건을 새로 만들어 채운다.
            var created = new InputCondition();
            link.Condition = created;
            return created;
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
            DrawSeparator();

            EditorGUI.BeginChangeCheck();
            var   type   = (NotifyType)EditorGUILayout.EnumPopup("Type",  notify.Type);
            float normT  = FrameField("Time (f)", "이 Notify가 발동하는 프레임", tc, notify.NormalizedTime);
            string eName = EditorGUILayout.TextField("Event Name",   notify.EventName);
            GameObject prefab = notify.EffectPrefab;
            if (type == NotifyType.Effect)
                prefab = (GameObject)EditorGUILayout.ObjectField(
                    "Effect Prefab", notify.EffectPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Edit Notify");
                notify.Type = type; notify.NormalizedTime = normT;
                notify.EventName = eName; notify.EffectPrefab = prefab;
                EditorUtility.SetDirty(_config);
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

        private static void DrawSeparator()
        {
            EditorGUILayout.Space(2);
            var r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.3f, 0.3f, 0.3f, 0.5f));
        }
    }
}
