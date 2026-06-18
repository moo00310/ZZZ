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

            float done  = EditorGUILayout.Slider("OnEnd 발동 (0=마지막프레임)", _config.DoneThreshold, 0f, 1f);

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
            EditorGUILayout.LabelField("Root Motion", EditorStyles.boldLabel);
            if (_target != null && _target.GetComponentInChildren<ZZZ.Player.PlayerController>() != null)
                EditorGUILayout.LabelField("PlayerController에서 자동 감지됨", EditorStyles.miniLabel);

            _rootBone   = (Transform)EditorGUILayout.ObjectField("Root Bone",  _rootBone,   typeof(Transform), true);
            _bip001Bone = (Transform)EditorGUILayout.ObjectField("Bip001 Bone", _bip001Bone, typeof(Transform), true);
            _rootMotionScale = EditorGUILayout.FloatField("RM Scale", _rootMotionScale);

            EditorGUILayout.HelpBox(
                "클립 Move Mode = RootMotion이면\n루트본 이동량이 GameObject에 적용됩니다.",
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
                if (m is IFrameModule ifm)
                {
                    float s = ifm.Start, e = ifm.End;
                    EditorGUILayout.MinMaxSlider(
                        new GUIContent($"   Window  {s:F2}~{e:F2}", "무적이 작동하는 normalizedTime 구간"),
                        ref s, ref e, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Edit Module");
                        ifm.Start = Mathf.Clamp01(Mathf.Min(s, e));
                        ifm.End   = Mathf.Clamp01(Mathf.Max(s, e));
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
            using (new EditorGUI.DisabledScope(tc.Modules.Exists(x => x is IFrameModule)))
                if (GUILayout.Button("I-Frame", GUILayout.Width(80)))
                {
                    Undo.RecordObject(_config, "Add Module");
                    tc.Modules.Add(new IFrameModule());
                    EditorUtility.SetDirty(_config);
                }
            EditorGUILayout.EndHorizontal();
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
            bool faceInput = EditorGUILayout.Toggle(
                new GUIContent("Face Input On Enter", "진입 순간 이동 입력 방향으로 즉시 스냅 (공격 첫 프레임 조준). Lock Rotation과 함께 쓰면 진입 때 한 번 조준 후 고정"),
                tc.FaceInputOnEnter);

            // ── 고급 (접기) — Boost / Target Tracking / Section Turn ──
            // 값은 항상 현재값으로 초기화 → 접혀서 UI를 안 그려도 저장 로직이 그대로 유지됨
            float boostSpd = tc.StartBoostSpeed,  boostT = tc.StartBoostTime;
            bool  track    = tc.EnableTracking,   snap   = tc.SnapRotation;
            float twS = tc.TrackWindowStart, twE = tc.TrackWindowEnd, stopD = tc.StopDistance;
            bool  secTurn  = tc.SectionTurn;
            float turnAng  = tc.TurnAngle, swS = tc.TurnWindowStart, swE = tc.TurnWindowEnd;

            // 접혀 있어도 어떤 고급 옵션이 켜져 있는지 라벨로 표시
            string advLabel = "고급";
            if (boostSpd > 0f) advLabel += " · Boost";
            if (track)         advLabel += " · Track";
            if (secTurn)       advLabel += " · Turn";
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
                    track = EditorGUILayout.Toggle(
                        new GUIContent("Target Tracking", "전방 적이 있으면 루트모션을 적 방향으로 워프. 없으면 원본 그대로"),
                        tc.EnableTracking);
                    if (track)
                    {
                        EditorGUILayout.MinMaxSlider(
                            new GUIContent($"  Window  {twS:F2}~{twE:F2}", "워프가 작동하는 normalizedTime 구간. 타격 이후엔 끊을 것"),
                            ref twS, ref twE, 0f, 1f);
                        stopD = EditorGUILayout.FloatField(
                            new GUIContent("  Stop Distance", "타겟 앞 정지 거리 (관통 방지)"), tc.StopDistance);
                        snap = EditorGUILayout.Toggle(
                            new GUIContent("  Snap Rotation", "섹션 진입 시 타겟 방향으로 즉시 회전"), tc.SnapRotation);
                    }
                }

                secTurn = EditorGUILayout.Toggle(
                    new GUIContent("Section Turn", "윈도우 동안 bip001(몸통)을 정해진 각도만큼 회전 — 섹션 종료 시 복귀 (루트/카메라 영향 없음)"),
                    tc.SectionTurn);
                if (secTurn)
                {
                    turnAng = EditorGUILayout.FloatField(
                        new GUIContent("  Turn Angle", "구간 동안 누적 회전할 총 각도(도). + 오른쪽 / - 왼쪽"), tc.TurnAngle);
                    EditorGUILayout.MinMaxSlider(
                        new GUIContent($"  Window  {swS:F2}~{swE:F2}", "회전이 작동하는 normalizedTime 구간"),
                        ref swS, ref swE, 0f, 1f);
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
                tc.FaceInputOnEnter = faceInput;
                tc.StartBoostSpeed = Mathf.Max(0f, boostSpd);
                tc.StartBoostTime  = Mathf.Max(0.01f, boostT);
                tc.EnableTracking   = track;
                tc.TrackWindowStart = Mathf.Clamp01(Mathf.Min(twS, twE));
                tc.TrackWindowEnd   = Mathf.Clamp01(Mathf.Max(twS, twE));
                tc.StopDistance     = Mathf.Max(0f, stopD);
                tc.SnapRotation     = snap;
                tc.SectionTurn     = secTurn;
                tc.TurnAngle       = turnAng;
                tc.TurnWindowStart = Mathf.Clamp01(Mathf.Min(swS, swE));
                tc.TurnWindowEnd   = Mathf.Clamp01(Mathf.Max(swS, swE));
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
                if (link.Attack != ComboInput.None)
                    DrawChip(link.Attack.ToString(), k_chipAttack);
                if (link.Direction != MoveDir.Any)
                    DrawChip(link.Direction.ToString(), k_chipDir);
                if (link.Timing == LinkTiming.OnWindowMiss)
                    DrawChip("miss", k_chipWhenMiss);
                else if (link.Timing == LinkTiming.OnEnd)
                    DrawChip("end", k_chipWhen);
                if (link.Attack == ComboInput.None && link.Direction == MoveDir.Any
                    && link.Timing == LinkTiming.WhenMatched)
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

                    var attack = (ComboInput)ColoredEnum(new GUIContent("Attack"), k_chipAttack, link.Attack);
                    var dir    = (MoveDir)ColoredEnum(new GUIContent("Direction"), k_chipDir, link.Direction);
                    var timing = (LinkTiming)ColoredEnum(
                        new GUIContent("When", TimingHelp(link.Timing)), k_chipWhen, link.Timing);

                    float ws = link.WindowStart, we = link.WindowEnd;
                    if (timing != LinkTiming.OnEnd)   // OnEnd는 윈도우 불필요
                        EditorGUILayout.MinMaxSlider(
                            new GUIContent($"Window {ws:F2}-{we:F2}"), ref ws, ref we, 0f, 1f);

                    float blend = EditorGUILayout.FloatField("Blend (s)", link.BlendDuration);

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Edit Link");
                        link.TargetConfig  = targetCfg;
                        link.TargetSection = newIdx == 0 ? "" : sectionOptions[newIdx];
                        link.Attack        = attack;
                        link.Direction     = dir;
                        link.Timing        = timing;
                        link.WindowStart   = ws;
                        link.WindowEnd     = we;
                        link.BlendDuration = Mathf.Max(0f, blend);
                        EditorUtility.SetDirty(_config);
                    }
                }
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("+ Link 추가"))
                AddLink(links, new ClipLink());
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
            links.Add(link);
            EditorUtility.SetDirty(_config);
            Repaint();
        }

        // 링크 조건을 짧은 문자열로 (타임라인/인스펙터 공용)
        private static string CondLabel(ClipLink link)
        {
            string cond = "";
            if (link.Attack != ComboInput.None) cond = link.Attack.ToString();
            if (link.Direction != MoveDir.Any)
                cond = string.IsNullOrEmpty(cond) ? link.Direction.ToString()
                                                  : cond + "+" + link.Direction;
            if (string.IsNullOrEmpty(cond)) cond = "무조건";

            string suffix = link.Timing switch
            {
                LinkTiming.OnWindowMiss => " (miss)",
                LinkTiming.OnEnd        => " (end)",
                _                        => "",
            };
            return cond + suffix;
        }

        private static string TimingHelp(LinkTiming t) => t switch
        {
            LinkTiming.WhenMatched  => "윈도우 안에서 조건 충족 시 즉시 전이",
            LinkTiming.OnWindowMiss => "윈도우 끝까지 조건 유지되면 전이 (캔슬/타임아웃)",
            LinkTiming.OnEnd        => "클립이 끝나면 전이 (루프 클립 제외)",
            _                        => "",
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
            float normT  = EditorGUILayout.Slider("Normalized Time", notify.NormalizedTime, 0f, 1f);
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
