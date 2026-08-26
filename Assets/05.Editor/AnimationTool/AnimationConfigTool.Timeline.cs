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
            float contentRowsH = TimelineContentHeight() + 34f;
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
                float aRowY = ClipRowTop(_comboActiveClip) - _scrollY;
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
                float lRowY = ClipRowTop(_liveClipIdx) - _scrollY;
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
                float rowY    = ClipRowTop(i) - _scrollY;
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
                if (ModulesExpanded(tc))
                    DrawModuleLanes(tc, barX, barW, rowY + ClipH, area.width);
            }

            // 순서 변경 삽입 위치 표시선
            if (_reorderingClip >= 0 && _reorderTargetIdx >= 0)
            {
                float lineY = ClipInsertionY(_reorderTargetIdx) - _scrollY - 1f;
                EditorGUI.DrawRect(new Rect(0, lineY, area.width, 3f), new Color(0.3f, 0.65f, 1f));
            }

            // 클립 추가 버튼
            float addY = TimelineContentHeight() - _scrollY + 6f;
            if (addY < rowsH && GUI.Button(new Rect(4, addY, 110, 22), "+ Add Section"))
            {
                Undo.RecordObject(_config, "Add Section");
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

        private bool ModulesExpanded(TrackClip tc)
            => tc != null && tc.Modules != null && tc.Modules.Count > 0
                && _expandedModuleClips.Contains(tc);

        private float ExpandedModuleHeight(TrackClip tc)
            => ModulesExpanded(tc) ? tc.Modules.Count * ModuleLaneH : 0f;

        private float ClipRowTop(int index)
        {
            float y = 0f;
            int count = Mathf.Min(index, _config.Clips.Count);
            for (int i = 0; i < count; i++)
                y += ClipH + ExpandedModuleHeight(_config.Clips[i]) + ClipGap;
            return y;
        }

        private float ClipInsertionY(int index)
            => index >= _config.Clips.Count ? TimelineContentHeight() : ClipRowTop(index);

        private float TimelineContentHeight()
            => ClipRowTop(_config.Clips.Count);

        private int ClipInsertionIndexAt(float contentY)
        {
            for (int i = 0; i < _config.Clips.Count; i++)
            {
                float midpoint = ClipRowTop(i)
                    + (ClipH + ExpandedModuleHeight(_config.Clips[i]) + ClipGap) * 0.5f;
                if (contentY < midpoint) return i;
            }
            return _config.Clips.Count;
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
            float rowMid = ClipRowTop(_liveClipIdx) + ClipH * 0.5f;
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
                float srcYc  = ClipRowTop(i) - _scrollY + ClipH * 0.5f;

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

                    // 출발 지점: WhenMatched/OnRelease=윈도우끝, OnEnd=클립끝
                    float srcN = link.Timing switch
                    {
                        LinkTiming.WhenMatched    => link.WindowEnd,
                        LinkTiming.OnRelease      => link.WindowEnd,
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
                    float dstY = ClipRowTop(ti) - _scrollY + ClipH * 0.5f;

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
            if (tc.Modules != null && tc.Modules.Count > 0)
            {
                bool expanded = ModulesExpanded(tc);
                if (GUI.Button(new Rect(72, rowY + 21, 64, 14),
                    $"{(expanded ? "▼" : "▶")} M {tc.Modules.Count}",
                    new GUIStyle(EditorStyles.miniButton) { fontSize = 8 }))
                {
                    if (expanded) _expandedModuleClips.Remove(tc);
                    else _expandedModuleClips.Add(tc);
                    Repaint();
                }
            }

            if (tc.Clip != null)
                GUI.Label(new Rect(7, rowY + 38, LabelW - 26, 11),
                    $"{tc.Clip.length / Mathf.Max(0.01f, tc.Speed):F2}s  x{tc.Speed:F1}",
                    new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = new Color(0.52f, 0.52f, 0.52f) } });

            if (GUI.Button(new Rect(LabelW - 18, rowY + 3, 15, 15), "×",
                new GUIStyle(EditorStyles.miniButton) { fontSize = 9 }))
            {
                Undo.RecordObject(_config, "Remove Clip");
                _expandedModuleClips.Remove(tc);
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

                if (!ModulesExpanded(tc))
                    DrawCollapsedModuleSummary(tc, barX, barW, rowY);

                // Link 윈도우 밴드 (바 하단)
                DrawLinkWindows(tc, barX, barW, rowY);
            }

            // Notify 마커
            if (tc.Clip != null)
                for (int ni = 0; ni < tc.Notifies.Count; ni++)
                    DrawNotifyMarker(tc.Notifies[ni], ni, idx, barX, barW, rowY);
        }

        private void DrawModuleLanes(TrackClip tc, float barX, float barW,
            float lanesY, float totalW)
        {
            for (int i = 0; i < tc.Modules.Count; i++)
            {
                SectionModule module = tc.Modules[i];
                float y = lanesY + i * ModuleLaneH;
                Color color = ModuleColor(module);

                EditorGUI.DrawRect(new Rect(0f, y, totalW, ModuleLaneH - 1f),
                    i % 2 == 0 ? new Color(0.145f, 0.145f, 0.145f) : new Color(0.16f, 0.16f, 0.16f));
                EditorGUI.DrawRect(new Rect(0f, y, 3f, ModuleLaneH - 1f), color);

                string label = module != null ? module.MenuName : "(Missing Module)";
                GUI.Label(new Rect(7f, y + 1f, LabelW - 10f, ModuleLaneH - 2f), label,
                    new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 9,
                        clipping = TextClipping.Clip,
                        normal = { textColor = color }
                    });

                if (module == null || barW <= 0f || barX + barW <= LabelW || barX >= totalW)
                    continue;

                float laneTop = y + 3f;
                float laneHeight = ModuleLaneH - 7f;
                if (module is WindowModule window)
                {
                    float start = Mathf.Clamp01(window.Start);
                    float end = Mathf.Clamp01(window.End);
                    if (Mathf.Approximately(start, end))
                    {
                        float x = Mathf.Clamp(barX + start * barW, LabelW, totalW);
                        EditorGUI.DrawRect(new Rect(x - 1f, laneTop, 3f, laneHeight), color);
                        GUI.Label(new Rect(x + 4f, y + 1f, Mathf.Max(0f, barW - 6f),
                            ModuleLaneH - 2f), module.DisplayName, ModuleLaneLabelStyle(color));
                        DrawModuleHandle(x, laneTop, laneHeight, color,
                            _dragWindowModule == window);
                    }
                    else
                    {
                        float actualStartX = barX + start * barW;
                        float actualEndX = barX + end * barW;
                        float startX = Mathf.Max(LabelW, actualStartX);
                        float endX = Mathf.Min(totalW, actualEndX);
                        DrawModuleRange(startX, endX,
                            laneTop, laneHeight, color, module.DisplayName);
                        if (actualStartX >= LabelW && actualStartX <= totalW)
                            DrawModuleHandle(actualStartX, laneTop, laneHeight, color,
                                _dragWindowModule == window && _dragWindowStart);
                        if (actualEndX >= LabelW && actualEndX <= totalW)
                            DrawModuleHandle(actualEndX, laneTop, laneHeight, color,
                                _dragWindowModule == window && !_dragWindowStart);
                    }
                }
                else if (module is FaceInputModule || module is StartBoostModule)
                {
                    float x = Mathf.Max(LabelW, barX);
                    EditorGUI.DrawRect(new Rect(x - 1f, laneTop, 3f, laneHeight), color);
                    GUI.Label(new Rect(x + 4f, y + 1f, Mathf.Max(0f, barW - 6f), ModuleLaneH - 2f),
                        module.DisplayName, ModuleLaneLabelStyle(color));
                }
                else
                {
                    DrawModuleRange(Mathf.Max(LabelW, barX), Mathf.Min(totalW, barX + barW),
                        laneTop, laneHeight, color,
                        module.DisplayName);
                }
            }
        }

        private void DrawCollapsedModuleSummary(TrackClip tc, float barX, float barW, float rowY)
        {
            if (tc.Modules == null || tc.Modules.Count == 0 || barW <= 0f) return;

            int visible = Mathf.Min(tc.Modules.Count, 5);
            for (int i = 0; i < visible; i++)
            {
                SectionModule module = tc.Modules[i];
                Color color = ModuleColor(module);
                float y = rowY + 23f + i * 3f;

                if (module is WindowModule window)
                {
                    float start = Mathf.Clamp01(window.Start);
                    float end = Mathf.Clamp01(window.End);
                    EditorGUI.DrawRect(new Rect(
                        barX + start * barW, y, Mathf.Max(2f, (end - start) * barW), 2f), color);
                }
                else
                    EditorGUI.DrawRect(new Rect(barX, y, Mathf.Max(2f, barW), 2f), color);
            }
        }

        private static void DrawModuleRange(float startX, float endX, float y, float height,
            Color color, string label)
        {
            if (endX <= startX) return;
            float width = Mathf.Max(2f, endX - startX);
            EditorGUI.DrawRect(new Rect(startX, y, width, height),
                new Color(color.r, color.g, color.b, 0.25f));
            EditorGUI.DrawRect(new Rect(startX, y, 2f, height), color);
            EditorGUI.DrawRect(new Rect(endX - 2f, y, 2f, height), color);
            if (width > 54f)
                GUI.Label(new Rect(startX + 4f, y - 2f, width - 8f, height + 4f),
                    label, ModuleLaneLabelStyle(color));
        }

        private static void DrawModuleHandle(float x, float y, float height, Color color, bool active)
        {
            var hitRect = new Rect(x - 6f, y - 2f, 12f, height + 4f);
            EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.ResizeHorizontal);
            Color handleColor = active ? Color.white : color;
            EditorGUI.DrawRect(new Rect(x - 2f, y - 1f, 4f, height + 2f), handleColor);
            EditorGUI.DrawRect(new Rect(x - 4f, y - 2f, 8f, 2f), handleColor);
        }

        private static GUIStyle ModuleLaneLabelStyle(Color color)
            => new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 8,
                clipping = TextClipping.Clip,
                normal = { textColor = color }
            };

        private static Color ModuleColor(SectionModule module)
        {
            if (module is AdditionalMovementModule || module is StartBoostModule
                || module is BackMotionScaleModule)
                return new Color(0.3f, 0.65f, 1f);
            if (module is TargetWarpModule)
                return new Color(0.2f, 0.85f, 0.8f);
            if (module is RotationLockModule || module is FaceInputModule
                || module is FaceTargetModule || module is SectionTurnModule)
                return new Color(0.85f, 0.5f, 1f);
            if (module is IFrameModule || module is ParryModule)
                return new Color(0.35f, 0.9f, 0.45f);
            return new Color(0.7f, 0.7f, 0.7f);
        }

        // 입력 타입별 색상
        private static Color InputColor(ComboInput input) => input switch
        {
            ComboInput.Normal  => new Color(0.3f, 0.6f, 1.0f),
            ComboInput.Strong  => new Color(1.0f, 0.55f, 0.15f),   // 강공격 = 주황
            ComboInput.Enhance => new Color(0.9f, 0.3f, 0.9f),
            ComboInput.Dodge   => new Color(0.3f, 0.9f, 0.6f),
            ComboInput.None    => new Color(0.5f, 0.85f, 0.55f),   // 공격 없음 = 초록
            _                  => new Color(0.7f, 0.7f, 0.7f),     // Any / Parry
        };

        // 링크 색상: OnRelease=청록(키 릴리스), OnEnd=회색, 그 외는 공격/방향 조건 색
        private static Color LinkColor(ClipLink link)
        {
            switch (link.Timing)
            {
                case LinkTiming.OnRelease:    return new Color(0.35f, 0.8f, 0.9f);
                case LinkTiming.OnEnd:        return new Color(0.75f, 0.75f, 0.75f);
                default:                      return InputColor(ReadInput(link)?.Attack ?? ComboInput.None);
            }
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

                // 밴드 구간: WhenMatched/OnRelease=윈도우(릴리스 허용 구간), OnEnd=끝부분
                float aN, bN;
                switch (link.Timing)
                {
                    case LinkTiming.WhenMatched:    aN = link.WindowStart; bN = link.WindowEnd; break;
                    case LinkTiming.OnRelease:      aN = link.WindowStart; bN = link.WindowEnd; break;  // 릴리스 허용 윈도우
                    case LinkTiming.OnEnd:          aN = 0.92f;            bN = 1f;             break;
                    case LinkTiming.OnEndIfMatched: aN = link.WindowStart; bN = link.WindowEnd; break;  // 입력 감지 윈도우
                    default:                        aN = 0f;               bN = 1f;             break;
                }
                float aX = barX + aN * barW;
                float bX = barX + bN * barW;
                EditorGUI.DrawRect(new Rect(aX, y, Mathf.Max(2f, bX - aX), bandH),
                    new Color(col.r, col.g, col.b, 0.75f));
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

            // 세로선
            EditorGUI.DrawRect(new Rect(mx - 1f, my, 2f, mh), col);

            // 클릭 타겟이 되는 '깃발 머리' — 클릭 허용 반경(±NotifyHitRadius)과 맞춰 넉넉하게.
            float hw = NotifyHitRadius;            // half-width
            var head = new Rect(mx - hw, my - 9f, hw * 2f, 9f);
            if (sel)   // 선택 시 흰 외곽선으로 강조
                EditorGUI.DrawRect(new Rect(head.x - 1f, head.y - 1f, head.width + 2f, head.height + 2f),
                    new Color(1f, 1f, 1f, 0.9f));
            EditorGUI.DrawRect(head, col);
            // 고정 시: 어두운 외곽 링으로 '잠김' 표시(이모지는 IMGUI 폰트에서 깨지므로 도형으로)
            if (notify.Locked)
            {
                var d = new Color(0.08f, 0.08f, 0.08f);
                EditorGUI.DrawRect(new Rect(head.x,               head.y,                head.width, 1f), d);
                EditorGUI.DrawRect(new Rect(head.x,               head.yMax - 1f,        head.width, 1f), d);
                EditorGUI.DrawRect(new Rect(head.x,               head.y,                1f, head.height), d);
                EditorGUI.DrawRect(new Rect(head.xMax - 1f,       head.y,                1f, head.height), d);
            }

            string icon = notify.Type switch
            {
                NotifyType.Effect => "E",
                NotifyType.Hit    => "H",
                NotifyType.Camera => "C",
                NotifyType.Sound  => "S",
                _                 => "N",
            };
            GUI.Label(head, icon,
                new GUIStyle(EditorStyles.miniLabel)
                { alignment = TextAnchor.MiddleCenter, fontSize = 8,
                  normal = { textColor = new Color(0.1f, 0.1f, 0.1f) } });

            ConfigEventType configEvent = notify.ConfigEvent;
            if (_pxPerSec > 60f
                && configEvent != ConfigEventType.None)
            {
                string label = configEvent.ToString();
                if (label.Length > 8) label = label.Substring(0, 8);
                GUI.Label(new Rect(mx + 3, my + 2, 60, 10), label,
                    new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = col }, fontSize = 8 });
            }
        }
    }
}
