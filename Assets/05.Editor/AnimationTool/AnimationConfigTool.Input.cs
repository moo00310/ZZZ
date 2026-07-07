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
        // ── 눈금자 플레이헤드 드래그 ─────────────────────────────
        private void HandleRulerInput(Rect rulerRect)
        {
            var ev = Event.current;

            if (ev.type == EventType.MouseDown && ev.button == 0 && rulerRect.Contains(ev.mousePosition))
            {
                _draggingPlayhead = true;
                float t = (ev.mousePosition.x - rulerRect.x + _scrollX) / _pxPerSec;
                _trackTime = Mathf.Clamp(t, 0f, GetTotalDuration());
                StopPreview(); SampleAtTime(_trackTime, false);
                ev.Use(); Repaint();
            }
            if (ev.type == EventType.MouseDrag && _draggingPlayhead)
            {
                float t = (ev.mousePosition.x - rulerRect.x + _scrollX) / _pxPerSec;
                _trackTime = Mathf.Clamp(t, 0f, GetTotalDuration());
                StopPreview(); SampleAtTime(_trackTime, false);
                ev.Use(); Repaint();
            }
            if (ev.type == EventType.MouseUp) _draggingPlayhead = false;
        }
        // ── 입력 처리 ────────────────────────────────────────────
        private void HandleInput(Rect area)
        {
            var ev = Event.current;

            // ── MouseDown ────────────────────────────────────────
            if (ev.type == EventType.MouseDown && ev.button == 0 && area.Contains(ev.mousePosition))
            {
                bool hitSomething = false;

                for (int i = 0; i < _config.Clips.Count && !hitSomething; i++)
                {
                    float rowY = i * (ClipH + ClipGap) - _scrollY;
                    if (ev.mousePosition.y < rowY || ev.mousePosition.y >= rowY + ClipH) continue;

                    // 레이블 영역 클릭 → 순서 변경 드래그 시작
                    if (ev.mousePosition.x < LabelW)
                    {
                        _reorderingClip   = i;
                        _reorderTargetIdx = i;
                        _selectedClip     = i;
                        _selectedNotify   = -1;
                        hitSomething      = true;
                        ev.Use(); Repaint();
                        break;
                    }

                    var   tc     = _config.Clips[i];
                    float startT = GetClipStartTime(i);
                    float dur    = tc.Clip != null ? tc.Clip.length / Mathf.Max(0.01f, tc.Speed) : 0f;
                    float barX   = LabelW + startT * _pxPerSec - _scrollX;
                    float barW   = dur * _pxPerSec;

                    // Notify 클릭 — 허용 반경(±NotifyHitRadius) 안의 마커를 후보로 모은다.
                    // 겹쳐 있으면 같은 자리를 다시 클릭할 때마다 다음 마커로 순환 선택(cycle)해
                    // 스택된 Notify도 하나씩 집을 수 있게 한다.
                    if (tc.Clip != null)
                    {
                        _notifyHitBuf.Clear();
                        for (int ni = 0; ni < tc.Notifies.Count; ni++)
                        {
                            float mx = barX + tc.Notifies[ni].NormalizedTime * barW;
                            if (Mathf.Abs(ev.mousePosition.x - mx) <= NotifyHitRadius)
                                _notifyHitBuf.Add(ni);
                        }
                        if (_notifyHitBuf.Count > 0)
                        {
                            int pick;
                            int curPos = (_notifyClipIdx == i) ? _notifyHitBuf.IndexOf(_selectedNotify) : -1;
                            if (curPos >= 0)
                                pick = _notifyHitBuf[(curPos + 1) % _notifyHitBuf.Count];   // 다음 후보로 순환
                            else
                            {
                                // 첫 클릭: 가장 가까운 마커
                                pick = _notifyHitBuf[0];
                                float best = float.MaxValue;
                                foreach (int ni in _notifyHitBuf)
                                {
                                    float dx = Mathf.Abs(ev.mousePosition.x - (barX + tc.Notifies[ni].NormalizedTime * barW));
                                    if (dx < best) { best = dx; pick = ni; }
                                }
                            }

                            _selectedClip = i; _selectedNotify = pick; _notifyClipIdx = i;
                            // 고정된 Notify는 선택만 하고 드래그(이동)는 시작하지 않는다.
                            if (!tc.Notifies[pick].Locked)
                            {
                                _draggingNotify = true; _dragNotifyClip = i; _dragNotifyIdx = pick;
                            }
                            hitSomething = true;
                        }
                    }

                    if (!hitSomething) { _selectedClip = i; _selectedNotify = -1; hitSomething = true; }

                    // 타임라인 영역 클릭 → 플레이헤드 이동
                    if (ev.mousePosition.x >= LabelW)
                    {
                        float newT = (ev.mousePosition.x - LabelW + _scrollX) / _pxPerSec;
                        _trackTime = Mathf.Clamp(newT, 0f, GetTotalDuration());
                        StopPreview(); SampleAtTime(_trackTime, false);
                    }
                }

                if (!hitSomething && ev.mousePosition.x >= LabelW)
                {
                    float newT = (ev.mousePosition.x - LabelW + _scrollX) / _pxPerSec;
                    _trackTime = Mathf.Clamp(newT, 0f, GetTotalDuration());
                    StopPreview(); SampleAtTime(_trackTime, false);
                    _selectedClip = -1; _selectedNotify = -1;
                }

                ev.Use(); Repaint();
            }

            // ── MouseDrag ────────────────────────────────────────
            if (ev.type == EventType.MouseDrag && ev.button == 0)
            {
                // Notify 드래그
                if (_draggingNotify && _dragNotifyClip < _config.Clips.Count)
                {
                    var   tc     = _config.Clips[_dragNotifyClip];
                    float startT = GetClipStartTime(_dragNotifyClip);
                    float dur    = tc.Clip != null ? tc.Clip.length / Mathf.Max(0.01f, tc.Speed) : 1f;
                    float barX   = LabelW + startT * _pxPerSec - _scrollX;
                    float barW   = dur * _pxPerSec;
                    float newN   = barW > 0f ? Mathf.Clamp01((ev.mousePosition.x - barX) / barW) : 0f;
                    Undo.RecordObject(_config, "Move Notify");
                    tc.Notifies[_dragNotifyIdx].NormalizedTime = newN;
                    EditorUtility.SetDirty(_config);
                    // Effect 탭: 발동 시점이 바뀌었으니 현재 플레이헤드에서 이펙트 재시뮬레이션
                    if (EffectPreviewActive) SampleAtTime(_trackTime, false);
                    ev.Use(); Repaint();
                }
                // 클립 순서 변경 드래그
                else if (_reorderingClip >= 0)
                {
                    // 마우스 Y 위치로 삽입 인덱스 계산
                    int target = Mathf.Clamp(
                        Mathf.RoundToInt((ev.mousePosition.y + _scrollY) / (ClipH + ClipGap)),
                        0, _config.Clips.Count);
                    _reorderTargetIdx = target;
                    ev.Use(); Repaint();
                }
            }

            // ── MouseUp ──────────────────────────────────────────
            if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                _draggingNotify = false;

                if (_reorderingClip >= 0)
                {
                    int from = _reorderingClip;
                    int to   = _reorderTargetIdx;

                    // to가 from 이후를 가리킬 때 실제 삽입 인덱스 보정
                    int insertIdx = to > from ? to - 1 : to;

                    if (insertIdx != from && insertIdx >= 0 && insertIdx < _config.Clips.Count)
                    {
                        Undo.RecordObject(_config, "Reorder Clips");
                        var clip = _config.Clips[from];
                        _config.Clips.RemoveAt(from);
                        int clamp = Mathf.Clamp(insertIdx, 0, _config.Clips.Count);
                        _config.Clips.Insert(clamp, clip);
                        _selectedClip     = clamp;
                        EditorUtility.SetDirty(_config);
                        _serializedConfig = new SerializedObject(_config);
                    }

                    _reorderingClip   = -1;
                    _reorderTargetIdx = -1;
                    Repaint();
                }
            }

            // ── 우클릭: Notify 추가 ──────────────────────────────
            if (ev.type == EventType.ContextClick && area.Contains(ev.mousePosition))
            {
                for (int i = 0; i < _config.Clips.Count; i++)
                {
                    var tc = _config.Clips[i];
                    if (tc.Clip == null) continue;
                    float rowY   = i * (ClipH + ClipGap) - _scrollY;
                    float startT = GetClipStartTime(i);
                    float dur    = tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
                    float barX   = LabelW + startT * _pxPerSec - _scrollX;
                    float barW   = dur * _pxPerSec;

                    if (ev.mousePosition.y < rowY || ev.mousePosition.y >= rowY + ClipH) continue;
                    if (ev.mousePosition.x < barX  || ev.mousePosition.x > barX + barW)  continue;

                    // 기존 Notify 위에서 우클릭이면 → 잠금 토글/삭제 메뉴 (가장 가까운 마커 기준)
                    int   hitNi = -1;
                    float hitDx = NotifyHitRadius;
                    for (int ni = 0; ni < tc.Notifies.Count; ni++)
                    {
                        float nmx = barX + tc.Notifies[ni].NormalizedTime * barW;
                        float ndx = Mathf.Abs(ev.mousePosition.x - nmx);
                        if (ndx <= hitDx) { hitDx = ndx; hitNi = ni; }
                    }
                    if (hitNi >= 0)
                    {
                        int capHitI = i; int capHitNi = hitNi;
                        var n = tc.Notifies[hitNi];
                        var nmenu = new GenericMenu();
                        nmenu.AddItem(new GUIContent(n.Locked ? "Unlock (이동 잠금 해제)" : "Lock (이동 잠금)"),
                            n.Locked, () =>
                        {
                            Undo.RecordObject(_config, "Toggle Notify Lock");
                            _config.Clips[capHitI].Notifies[capHitNi].Locked =
                                !_config.Clips[capHitI].Notifies[capHitNi].Locked;
                            EditorUtility.SetDirty(_config);
                            Repaint();
                        });
                        nmenu.AddSeparator("");
                        nmenu.AddItem(new GUIContent("Delete Notify"), false, () =>
                        {
                            Undo.RecordObject(_config, "Delete Notify");
                            _config.Clips[capHitI].Notifies.RemoveAt(capHitNi);
                            if (_selectedNotify == capHitNi && _notifyClipIdx == capHitI) _selectedNotify = -1;
                            EditorUtility.SetDirty(_config);
                            _serializedConfig = new SerializedObject(_config);
                            Repaint();
                        });
                        nmenu.ShowAsContext();
                        ev.Use();
                        break;
                    }

                    float normT = barW > 0f ? Mathf.Clamp01((ev.mousePosition.x - barX) / barW) : 0f;
                    int capI = i; float capN = normT;
                    var menu = new GenericMenu();
                    foreach (NotifyType nt in Enum.GetValues(typeof(NotifyType)))
                    {
                        var capType = nt;
                        menu.AddItem(new GUIContent($"Add {nt} Notify"), false, () =>
                        {
                            Undo.RecordObject(_config, "Add Notify");
                            _config.Clips[capI].Notifies.Add(new TrackNotify
                            { Type = capType, NormalizedTime = capN, EventName = capType.ToString() });
                            _selectedClip   = capI;
                            _selectedNotify = _config.Clips[capI].Notifies.Count - 1;
                            _notifyClipIdx  = capI;
                            EditorUtility.SetDirty(_config);
                            _serializedConfig = new SerializedObject(_config);
                            Repaint();
                        });
                    }
                    menu.ShowAsContext();
                    ev.Use();
                    break;
                }
            }
        }
    }
}
