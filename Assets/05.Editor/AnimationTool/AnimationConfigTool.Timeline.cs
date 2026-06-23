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
        // ── Timeline ─────────────────────────────────────────────
        private void DrawTimeline(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.17f, 0.17f, 0.17f));

            if (_config == null)
            {
                GUI.Label(area, "Config를 선택하거나 New로 생성하세요.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel));
                return;
            }

            // 수직 스크롤 (휠) — 수평은 하단 스크롤바로 조정
            float contentRowsH = _config.Clips.Count * (ClipH + ClipGap) + 34f;   // +Add 버튼 여백 포함
            float viewRowsH    = area.height - RulerH - HScrollH;
            float maxScrollY   = Mathf.Max(0f, contentRowsH - viewRowsH);
            if (Event.current.type == EventType.ScrollWheel && area.Contains(Event.current.mousePosition))
            {
                _scrollY = Mathf.Clamp(_scrollY + Event.current.delta.y * 15f, 0f, maxScrollY);
                Event.current.Use(); Repaint();
            }
            _scrollY = Mathf.Clamp(_scrollY, 0f, maxScrollY);   // 클립 수 변동/리사이즈 대응

            // 라이브 모드: Follow가 켜져 있으면 런타임 플레이헤드/활성 행을 따라 스크롤
            FollowLivePlayhead(area.width - LabelW, viewRowsH, maxScrollY);

            // ── 눈금자 ────────────────────────────────────────────
            var rulerBg = new Rect(0, area.y, area.width, RulerH);
            EditorGUI.DrawRect(rulerBg, new Color(0.21f, 0.21f, 0.21f));
            EditorGUI.DrawRect(new Rect(0, area.y, LabelW, RulerH), new Color(0.18f, 0.18f, 0.18f));
            DrawRuler(new Rect(LabelW, area.y, area.width - LabelW, RulerH));

            // 플레이헤드 삼각형 + 선 (눈금자) — 순차 모드 & 비-플레이 중에만
            bool editPreview = !EditorApplication.isPlaying;
            float phX = LabelW + _trackTime * _pxPerSec - _scrollX;
            if (editPreview && !_comboMode && phX >= LabelW && phX <= area.width)
            {
                EditorGUI.DrawRect(new Rect(phX - 1f, area.y, 2f, RulerH), new Color(1f, 0.6f, 0f));
                DrawPlayheadHandle(phX, area.y, 7f, new Color(1f, 0.6f, 0f));
            }

            // 눈금자 플레이헤드 드래그 (순차 모드 & 비-플레이 중에만)
            if (editPreview && !_comboMode)
                HandleRulerInput(new Rect(LabelW, area.y, area.width - LabelW, RulerH));

            // ── 클립 행 영역 ──────────────────────────────────────
            float rowsY = area.y + RulerH;
            float rowsH = viewRowsH;   // 하단 스크롤바 높이만큼 제외
            GUI.BeginClip(new Rect(0, rowsY, area.width, rowsH));
            var localArea = new Rect(0, 0, area.width, rowsH);

            // 플레이헤드 세로선 (클립 영역) — 순차 모드 & 비-플레이 중에만
            if (editPreview && !_comboMode && phX >= LabelW && phX <= area.width)
                EditorGUI.DrawRect(new Rect(phX - 1f, 0, 2f, rowsH), new Color(1f, 0.6f, 0f, 0.6f));

            // 콤보 모드: 활성 클립 행 강조 + 로컬 플레이헤드 (비-플레이 중에만)
            if (editPreview && _comboMode && _comboActiveClip >= 0 && _comboActiveClip < _config.Clips.Count)
            {
                var   atc  = _config.Clips[_comboActiveClip];
                float aRowY = _comboActiveClip * (ClipH + ClipGap) - _scrollY;
                EditorGUI.DrawRect(new Rect(LabelW, aRowY, area.width - LabelW, ClipH),
                    new Color(1f, 0.6f, 0f, 0.08f));

                if (atc.Clip != null)
                {
                    float aStartT = GetClipStartTime(_comboActiveClip);
                    float aBarX   = LabelW + aStartT * _pxPerSec - _scrollX;
                    float aNt     = atc.Clip.length > 0f ? _comboClipTime / atc.Clip.length : 0f;
                    float aDur    = atc.Clip.length / Mathf.Max(0.01f, atc.Speed);
                    float aPhX    = aBarX + Mathf.Clamp01(aNt) * aDur * _pxPerSec;
                    EditorGUI.DrawRect(new Rect(aPhX - 1f, aRowY, 2f, ClipH), new Color(1f, 0.6f, 0f));
                }
            }

            // 라이브 모드: 런타임 활성 섹션 행 강조 + 런타임 플레이헤드 (초록)
            if (EditorApplication.isPlaying && _liveConfig == _config &&
                _liveClipIdx >= 0 && _liveClipIdx < _config.Clips.Count)
            {
                var   ltc   = _config.Clips[_liveClipIdx];
                float lRowY = _liveClipIdx * (ClipH + ClipGap) - _scrollY;
                EditorGUI.DrawRect(new Rect(LabelW, lRowY, area.width - LabelW, ClipH),
                    new Color(0.3f, 1f, 0.35f, 0.10f));

                if (ltc.Clip != null)
                {
                    float lStartT = GetClipStartTime(_liveClipIdx);
                    float lBarX   = LabelW + lStartT * _pxPerSec - _scrollX;
                    float lDur    = ltc.Clip.length / Mathf.Max(0.01f, ltc.Speed);
                    float lNt     = ltc.IsLooping ? Mathf.Repeat(_liveNt, 1f) : Mathf.Clamp01(_liveNt);
                    float lPhX    = lBarX + lNt * lDur * _pxPerSec;
                    EditorGUI.DrawRect(new Rect(lPhX - 1f, lRowY, 2f, ClipH), new Color(0.3f, 1f, 0.35f));
                }
            }

            // 전이 점선
            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI();
                DrawTransitionConnectors();
                Handles.EndGUI();
            }

            // 클립 행 그리기
            for (int i = 0; i < _config.Clips.Count; i++)
            {
                float rowY    = i * (ClipH + ClipGap) - _scrollY;
                float startT  = GetClipStartTime(i);
                var   tc      = _config.Clips[i];
                float dur     = tc.Clip != null ? tc.Clip.length / Mathf.Max(0.01f, tc.Speed) : 0f;
                float barX    = LabelW + startT * _pxPerSec - _scrollX;
                float barW    = dur * _pxPerSec;

                // 드래그 중인 클립은 반투명 처리
                bool isBeingDragged = _reorderingClip == i;
                if (isBeingDragged)
                    EditorGUI.DrawRect(new Rect(0, rowY, area.width, ClipH),
                        new Color(0f, 0f, 0f, 0.45f));

                DrawClipRow(tc, i, barX, barW, rowY, area.width);
            }

            // 순서 변경 삽입 위치 표시선
            if (_reorderingClip >= 0 && _reorderTargetIdx >= 0)
            {
                float lineY = _reorderTargetIdx * (ClipH + ClipGap) - _scrollY - 1f;
                EditorGUI.DrawRect(new Rect(0, lineY, area.width, 3f), new Color(0.3f, 0.65f, 1f));
            }

            // 클립 추가 버튼
            float addY = _config.Clips.Count * (ClipH + ClipGap) - _scrollY + 6f;
            if (addY < rowsH && GUI.Button(new Rect(4, addY, 100, 22), "+ Add Clip"))
            {
                Undo.RecordObject(_config, "Add Clip");
                _config.Clips.Add(new TrackClip());
                EditorUtility.SetDirty(_config);
                _serializedConfig = new SerializedObject(_config);
                Repaint();
            }

            HandleInput(localArea);
            GUI.EndClip();

            // ── 가로 스크롤바 (하단) ──────────────────────────────
            float contentW = GetTotalDuration() * _pxPerSec + 40f;   // 약간의 여백
            float viewW    = area.width - LabelW;
            var   hbarRect = new Rect(LabelW, area.y + area.height - HScrollH, viewW, HScrollH);
            EditorGUI.DrawRect(new Rect(0, hbarRect.y, area.width, HScrollH), new Color(0.18f, 0.18f, 0.18f));
            _scrollX = Mathf.Max(0f, GUI.HorizontalScrollbar(
                hbarRect, _scrollX, Mathf.Min(viewW, contentW), 0f, contentW));
        }
        // 라이브 프리뷰 중 런타임 플레이헤드/활성 행이 항상 뷰 중앙에 오도록 스크롤을 따라가게 한다.
        // Follow 토글과 연동 — 런타임 config를 따라가는 동안 가로(플레이헤드)·세로(활성 클립 행)를
        // 매 프레임 중앙 정렬한다. 양끝은 Clamp되어 콘텐츠 밖으로 넘어가지 않는다.
        private void FollowLivePlayhead(float viewW, float viewRowsH, float maxScrollY)
        {
            if (!_liveFollow || !EditorApplication.isPlaying) return;
            if (_liveConfig != _config || _liveClipIdx < 0 || _liveClipIdx >= _config.Clips.Count) return;

            var ltc = _config.Clips[_liveClipIdx];

            // ── 가로: 플레이헤드를 뷰 중앙에 ──
            if (ltc.Clip != null && viewW > 0f)
            {
                float lStartT = GetClipStartTime(_liveClipIdx);
                float lDur    = ltc.Clip.length / Mathf.Max(0.01f, ltc.Speed);
                float lNt     = ltc.IsLooping ? Mathf.Repeat(_liveNt, 1f) : Mathf.Clamp01(_liveNt);
                float phX     = (lStartT + lNt * lDur) * _pxPerSec;   // 트랙 원점 기준 X

                float contentW   = GetTotalDuration() * _pxPerSec + 40f;
                float maxScrollX = Mathf.Max(0f, contentW - viewW);
                _scrollX = Mathf.Clamp(phX - viewW * 0.5f, 0f, maxScrollX);
            }

            // ── 세로: 활성 클립 행을 뷰 중앙에 ──
            float rowMid = _liveClipIdx * (ClipH + ClipGap) + ClipH * 0.5f;
            _scrollY = Mathf.Clamp(rowMid - viewRowsH * 0.5f, 0f, maxScrollY);
        }

        private static void DrawPlayheadHandle(float x, float topY, float size, Color col)
        {
            // 아래를 향하는 삼각형
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            Handles.color = col;
            Handles.DrawAAConvexPolygon(
                new Vector3(x,        topY + size),
                new Vector3(x - size, topY),
                new Vector3(x + size, topY));
            Handles.EndGUI();
        }

        // ── 눈금자 ────────────────────────────────────────────────
        private void DrawRuler(Rect r)
        {
            float step   = _pxPerSec >= 100f ? 0.5f : _pxPerSec >= 50f ? 1f : 2f;
            float startT = _scrollX / _pxPerSec;
            float endT   = startT + r.width / _pxPerSec + step;

            for (float t = Mathf.Floor(startT / step) * step; t <= endT; t += step)
            {
                float x     = r.x + t * _pxPerSec - _scrollX;
                bool  major = Mathf.RoundToInt(t / step) % 5 == 0 || t == 0f;
                float th    = major ? 10f : 5f;
                EditorGUI.DrawRect(new Rect(x - 0.5f, r.y + r.height - th, 1f, th),
                    new Color(0.52f, 0.52f, 0.52f));
                if (major && t >= 0f)
                    GUI.Label(new Rect(x - 18f, r.y + 1f, 36f, 13f), $"{t:F1}s",
                        EditorStyles.centeredGreyMiniLabel);
            }
        }

        // ── Link 연결선 (윈도우 끝 → 타겟 섹션 행) ────────────────
        // 인스펙터에서 '편집 중인 링크'가 지정되면 그 링크 하나만 굵고 밝게(+라벨), 나머지는
        // 거의 안 보이게 → 곡선 겹침 제거. 링크 포커스가 없으면 선택 클립의 링크만 강조.
        private void DrawTransitionConnectors()
        {
            bool hasSel    = _selectedClip >= 0 && _selectedClip < _config.Clips.Count;
            bool linkFocus = hasSel && _selectedLink >= 0
                          && _selectedLink < _config.Clips[_selectedClip].Links.Count;

            for (int i = 0; i < _config.Clips.Count; i++)
            {
                var tc = _config.Clips[i];
                if (tc.Clip == null) continue;

                bool clipSel = hasSel && i == _selectedClip;

                float startT = GetClipStartTime(i);
                float dur    = tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
                float barX   = LabelW + startT * _pxPerSec - _scrollX;
                float barW   = dur * _pxPerSec;
                float srcYc  = i * (ClipH + ClipGap) - _scrollY + ClipH * 0.5f;

                for (int li = 0; li < tc.Links.Count; li++)
                {
                    // 강조 단계: 포커스 링크 > (포커스 없을 때) 선택 클립 링크 > 평상시
                    bool focused = clipSel && linkFocus && li == _selectedLink;
                    bool bright, dim;
                    if      (linkFocus) { bright = focused; dim = !focused; }
                    else if (hasSel)    { bright = clipSel; dim = !clipSel; }
                    else                { bright = false;   dim = false;    }

                    float lineW = focused ? 4.5f : bright ? 3f : dim ? 1f : 2f;
                    float alpha = bright ? 1f : dim ? 0.07f : 0.45f;
                    bool  arrow = bright || !hasSel;

                    var   link    = tc.Links[li];
                    Color baseCol = LinkColor(link);
                    // 포커스 링크는 흰색을 살짝 섞어 가시성↑
                    Color col = focused ? Color.Lerp(baseCol, Color.white, 0.35f) : baseCol;
                    Color c   = new Color(col.r, col.g, col.b, alpha);

                    // 출발 지점: WhenMatched/OnWindowMiss=윈도우끝, OnEnd=클립끝
                    float srcN = link.Timing switch
                    {
                        LinkTiming.WhenMatched    => link.WindowEnd,
                        LinkTiming.OnWindowMiss   => link.WindowEnd,
                        LinkTiming.OnEnd          => 1f,
                        LinkTiming.OnEndIfMatched => 1f,   // 래치는 섹션 끝에 발동
                        _                          => link.WindowEnd,
                    };
                    float sx = barX + srcN * barW;
                    // 같은 클립의 여러 링크가 겹치지 않게 출발 Y를 살짝 분산
                    float srcY = srcYc + (li - (tc.Links.Count - 1) * 0.5f) * 6f;

                    int ti = _config.IndexOfSection(link.TargetSection);
                    if (ti < 0)
                    {
                        // End/복귀: 아래로 짧게 떨어지는 점선
                        Handles.color = c;
                        Handles.DrawDottedLine(new Vector3(sx, srcY + 6f),
                            new Vector3(sx, srcY + ClipH * 0.5f), 3f);
                        if (focused)
                            DrawLinkLabel(sx, srcY + ClipH * 0.5f + 7f, $"{CondLabel(link)}→End", col);
                        continue;
                    }

                    float dstStartT = GetClipStartTime(ti);
                    float dstX = LabelW + dstStartT * _pxPerSec - _scrollX;
                    float dstY = ti * (ClipH + ClipGap) - _scrollY + ClipH * 0.5f;

                    float cdx = Mathf.Abs(dstY - srcY) * 0.4f + 24f;
                    Handles.DrawBezier(
                        new Vector3(sx, srcY), new Vector3(dstX, dstY),
                        new Vector3(sx + cdx, srcY), new Vector3(dstX - cdx, dstY),
                        c, null, lineW);

                    if (arrow)
                    {
                        Handles.color = c;
                        Handles.DrawAAConvexPolygon(
                            new Vector3(dstX, dstY),
                            new Vector3(dstX - 7f, dstY - 5f),
                            new Vector3(dstX - 7f, dstY + 5f));
                    }

                    // 포커스 링크만 베지어 중간에 (조건→대상) 라벨
                    if (focused)
                        DrawLinkLabel((sx + dstX) * 0.5f, (srcY + dstY) * 0.5f,
                            $"{CondLabel(link)}→{Short(link.TargetSection)}", col);
                }
            }
        }

        // 연결 보기 모드용 라벨 칩 — 어두운 배경 + 링크 색 텍스트로 베지어 위에서도 잘 읽히게
        private static void DrawLinkLabel(float cx, float cy, string text, Color col)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            { fontSize = 9, alignment = TextAnchor.MiddleCenter, normal = { textColor = col } };
            Vector2 sz = style.CalcSize(new GUIContent(text));
            var r = new Rect(cx - sz.x * 0.5f - 3f, cy - 7f, sz.x + 6f, 14f);
            EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.08f, 0.88f));
            GUI.Label(r, text, style);
        }

        // ── 클립 행 ───────────────────────────────────────────────
        private void DrawClipRow(TrackClip tc, int idx, float barX, float barW,
            float rowY, float totalW)
        {
            bool sel = idx == _selectedClip;

            // 레이블 배경
            EditorGUI.DrawRect(new Rect(0, rowY, LabelW - 1, ClipH),
                sel ? new Color(0.22f, 0.30f, 0.44f) : new Color(0.19f, 0.19f, 0.19f));

            // 드래그 핸들 표시 (좌측 3px 바)
            EditorGUI.DrawRect(new Rect(0, rowY + 2, 3, ClipH - 4),
                _reorderingClip == idx ? new Color(0.3f, 0.65f, 1f) : new Color(0.38f, 0.38f, 0.38f));

            string name = tc.Clip != null ? Short(tc.Clip.name) : "(No Clip)";
            GUI.Label(new Rect(7, rowY + 4, LabelW - 26, 15), name,
                new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 10, clipping = TextClipping.Clip,
                    normal = { textColor = sel ? Color.white : new Color(0.82f, 0.82f, 0.82f) }
                });

            DrawBadge("Loop", tc.IsLooping, new Rect(7, rowY + 22, 34, 12));
            DrawBadge("RM",   tc.UseRootMotion, new Rect(43, rowY + 22, 26, 12));

            if (tc.Clip != null)
                GUI.Label(new Rect(7, rowY + 38, LabelW - 26, 11),
                    $"{tc.Clip.length / Mathf.Max(0.01f, tc.Speed):F2}s  x{tc.Speed:F1}",
                    new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = new Color(0.52f, 0.52f, 0.52f) } });

            if (GUI.Button(new Rect(LabelW - 18, rowY + 3, 15, 15), "×",
                new GUIStyle(EditorStyles.miniButton) { fontSize = 9 }))
            {
                Undo.RecordObject(_config, "Remove Clip");
                _config.Clips.RemoveAt(idx);
                EditorUtility.SetDirty(_config);
                _serializedConfig = new SerializedObject(_config);
                if (_selectedClip >= _config.Clips.Count) _selectedClip = -1;
                Repaint();
                return;
            }

            // 클립 바
            if (barW > 0 && barX + barW > LabelW && barX < totalW)
            {
                Color barCol = sel ? new Color(0.28f, 0.46f, 0.78f) : new Color(0.30f, 0.30f, 0.34f);
                EditorGUI.DrawRect(new Rect(barX, rowY + 6, barW, ClipH - 12), barCol);
                EditorGUI.DrawRect(new Rect(barX, rowY + 6,           barW, 1f), new Color(0.55f, 0.55f, 0.60f));
                EditorGUI.DrawRect(new Rect(barX, rowY + ClipH - 7,   barW, 1f), new Color(0.55f, 0.55f, 0.60f));

                if (barW > 45f)
                    GUI.Label(new Rect(barX + 4, rowY + 8, barW - 8, 14), name,
                        new GUIStyle(EditorStyles.miniLabel)
                        { normal = { textColor = new Color(0.68f, 0.68f, 0.68f) },
                          clipping = TextClipping.Clip });

                // Section Turn(루트 회전) / Lock Rotation 윈도우 (바 위 밴드)
                DrawSectionTurnWindow(tc, barX, barW, rowY);
                DrawLockWindow(tc, barX, barW, rowY);

                // Link 윈도우 밴드 (바 하단)
                DrawLinkWindows(tc, barX, barW, rowY);
            }

            // Notify 마커
            if (tc.Clip != null)
                for (int ni = 0; ni < tc.Notifies.Count; ni++)
                    DrawNotifyMarker(tc.Notifies[ni], ni, idx, barX, barW, rowY);
        }

        // 입력 타입별 색상
        private static Color InputColor(ComboInput input) => input switch
        {
            ComboInput.Normal   => new Color(0.3f, 0.6f, 1.0f),
            ComboInput.Enhanced => new Color(1.0f, 0.55f, 0.15f),
            ComboInput.Special  => new Color(0.9f, 0.3f, 0.9f),
            ComboInput.Dodge    => new Color(0.3f, 0.9f, 0.6f),
            ComboInput.None     => new Color(0.5f, 0.85f, 0.55f),  // 공격 없음 = 초록
            _                   => new Color(0.7f, 0.7f, 0.7f),    // Any
        };

        // 링크 색상: OnWindowMiss=빨강(캔슬), OnEnd=회색, 그 외는 공격/방향 조건 색
        private static Color LinkColor(ClipLink link)
        {
            switch (link.Timing)
            {
                case LinkTiming.OnWindowMiss: return new Color(0.95f, 0.35f, 0.35f);
                case LinkTiming.OnEnd:        return new Color(0.75f, 0.75f, 0.75f);
                default:                      return InputColor(link.Attack);
            }
        }

        // Section Turn(루트 회전 추출) 윈도우 — [TurnWindowStart, End] 구간을 바 위에 보라 밴드로.
        private void DrawSectionTurnWindow(TrackClip tc, float barX, float barW, float rowY)
        {
            if (!tc.SectionTurn || barW <= 0f) return;
            DrawWindowBand(tc.TurnWindowStart, tc.TurnWindowEnd, barX, barW, rowY,
                new Color(0.72f, 0.45f, 1f), "Root Turn");   // 보라 = 회전
        }

        // Lock Rotation 윈도우 — [LockWindowStart, End] 구간을 바 위에 주황 밴드로.
        private void DrawLockWindow(TrackClip tc, float barX, float barW, float rowY)
        {
            if (!tc.LockRotation || barW <= 0f) return;
            DrawWindowBand(tc.LockWindowStart, tc.LockWindowEnd, barX, barW, rowY,
                new Color(1f, 0.6f, 0.2f), "Lock");          // 주황 = 잠금
        }

        // 바 위 [aN,bN] 구간을 반투명 밴드 + 양끝 경계 + 라벨로 그린다. End<=Start면 바 전체.
        private void DrawWindowBand(float startN, float endN, float barX, float barW, float rowY,
            Color col, string label)
        {
            bool whole = endN <= startN;
            float aN = whole ? 0f : Mathf.Clamp01(startN);
            float bN = whole ? 1f : Mathf.Clamp01(endN);
            float aX = barX + aN * barW;
            float bX = barX + bN * barW;
            float y  = rowY + 6f;
            float h  = ClipH - 12f;

            EditorGUI.DrawRect(new Rect(aX, y, Mathf.Max(2f, bX - aX), h),
                new Color(col.r, col.g, col.b, 0.16f));
            EditorGUI.DrawRect(new Rect(aX - 1f, y, 2f, h), col);   // 시작 경계
            EditorGUI.DrawRect(new Rect(bX - 1f, y, 2f, h), col);   // 끝 경계

            if (bX - aX > 34f)
                GUI.Label(new Rect(aX + 3f, y + 1f, bX - aX - 4f, 11f), label,
                    new GUIStyle(EditorStyles.miniLabel)
                    { fontSize = 8, normal = { textColor = col }, clipping = TextClipping.Clip });
        }

        // 클립 바 하단에 각 Link를 트리거별로 표시
        private void DrawLinkWindows(TrackClip tc, float barX, float barW, float rowY)
        {
            float bandH    = 5f;
            float baseY    = rowY + ClipH - 7f - bandH;

            for (int i = 0; i < tc.Links.Count; i++)
            {
                var   link = tc.Links[i];
                float y    = baseY - i * (bandH + 1f);
                Color col  = LinkColor(link);

                // 밴드 구간: WhenMatched=윈도우, OnWindowMiss=윈도우끝~이후, OnEnd=끝부분
                float aN, bN;
                switch (link.Timing)
                {
                    case LinkTiming.WhenMatched:    aN = link.WindowStart; bN = link.WindowEnd; break;
                    case LinkTiming.OnWindowMiss:   aN = link.WindowEnd;   bN = 1f;             break;
                    case LinkTiming.OnEnd:          aN = 0.92f;            bN = 1f;             break;
                    case LinkTiming.OnEndIfMatched: aN = link.WindowStart; bN = link.WindowEnd; break;  // 입력 감지 윈도우
                    default:                        aN = 0f;               bN = 1f;             break;
                }
                float aX = barX + aN * barW;
                float bX = barX + bN * barW;
                EditorGUI.DrawRect(new Rect(aX, y, Mathf.Max(2f, bX - aX), bandH),
                    new Color(col.r, col.g, col.b, 0.75f));
                // OnWindowMiss는 WindowEnd 지점에 마커
                if (link.Timing == LinkTiming.OnWindowMiss)
                    EditorGUI.DrawRect(new Rect(aX - 1f, y - 2f, 2f, bandH + 4f), col);
                // 텍스트 라벨은 제거(클립 이름 아래 한 줄에 대상만 표기) — 밴드만 남김
            }
        }

        private void DrawBadge(string label, bool active, Rect r)
        {
            Color col = active ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.38f, 0.38f, 0.38f);
            EditorGUI.DrawRect(r, new Color(col.r, col.g, col.b, 0.22f));
            GUI.Label(r, label, new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = col }, alignment = TextAnchor.MiddleCenter, fontSize = 9 });
        }

        // ── Notify 마커 ──────────────────────────────────────────
        private void DrawNotifyMarker(TrackNotify notify, int ni, int clipIdx,
            float barX, float barW, float rowY)
        {
            float mx  = barX + notify.NormalizedTime * barW;
            float my  = rowY + 6f;
            float mh  = ClipH - 12f;
            bool  sel = _selectedNotify == ni && _notifyClipIdx == clipIdx;
            Color col = sel ? Color.yellow : NotifyColors[(int)notify.Type % NotifyColors.Length];

            EditorGUI.DrawRect(new Rect(mx - 1f, my, 2f, mh), col);
            EditorGUI.DrawRect(new Rect(mx - 4f, my - 5f, 8f, 5f), col);

            string icon = notify.Type switch
            {
                NotifyType.Effect => "E",
                NotifyType.Camera => "C",
                NotifyType.Sound  => "S",
                _                 => "N",
            };
            GUI.Label(new Rect(mx - 5f, my - 5f, 10f, 10f), icon,
                new GUIStyle(EditorStyles.miniLabel)
                { alignment = TextAnchor.MiddleCenter, fontSize = 8,
                  normal = { textColor = new Color(0.1f, 0.1f, 0.1f) } });

            if (_pxPerSec > 60f && !string.IsNullOrEmpty(notify.EventName))
            {
                string lbl = notify.EventName.Length > 8
                    ? notify.EventName.Substring(0, 8) : notify.EventName;
                GUI.Label(new Rect(mx + 3, my + 2, 60, 10), lbl,
                    new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = col }, fontSize = 8 });
            }
        }
    }
}
