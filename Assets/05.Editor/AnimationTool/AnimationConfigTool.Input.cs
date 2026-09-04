using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ;
using ZZZ.Agent;

namespace ZZZ.Editor.AnimationTool
{
    public partial class AnimationConfigTool
    {
        private static TrackNotify _notifyClipboard;

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
                if (TrySelectCameraPathPoint(ev.mousePosition))
                {
                    ev.Use();
                    Repaint();
                    return;
                }

                if (TryBeginModuleWindowDrag(ev.mousePosition))
                {
                    ev.Use();
                    Repaint();
                    return;
                }

                bool hitSomething = false;

                for (int i = 0; i < _config.Clips.Count && !hitSomething; i++)
                {
                    float rowY = ClipRowTop(i) - _scrollY;
                    if (ev.mousePosition.y < rowY || ev.mousePosition.y >= rowY + ClipH) continue;

                    // 레이블 영역 클릭 → 순서 변경 드래그 시작
                    if (ev.mousePosition.x < LabelW)
                    {
                        _reorderingClip   = i;
                        _reorderTargetIdx = i;
                        _selectedClip     = i;
                        _selectedNotify   = -1;
                        FocusClipInTimeline(i, area.width - LabelW);
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
                            _selectedCameraPathPoint = -1;
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
                if (_draggingCameraPathPointTime)
                {
                    DragCameraPathPointTime(ev.mousePosition.x);
                    ev.Use();
                    Repaint();
                }
                else if (_dragWindowModule != null && _dragModuleClip != null)
                {
                    DragModuleWindow(ev.mousePosition.x);
                    ev.Use();
                    Repaint();
                }
                // Notify 드래그
                else if (_draggingNotify && _dragNotifyClip < _config.Clips.Count)
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
                    int target = ClipInsertionIndexAt(ev.mousePosition.y + _scrollY);
                    _reorderTargetIdx = target;
                    ev.Use(); Repaint();
                }
            }

            // ── MouseUp ──────────────────────────────────────────
            if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                _draggingNotify = false;
                _draggingCameraPathPointTime = false;
                _dragCameraPathPointIndex = -1;
                _dragModuleClip = null;
                _dragWindowModule = null;

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
                    float rowY   = ClipRowTop(i) - _scrollY;
                    float startT = GetClipStartTime(i);
                    float dur    = tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
                    float barX   = LabelW + startT * _pxPerSec - _scrollX;
                    float barW   = dur * _pxPerSec;

                    if (ev.mousePosition.y < rowY || ev.mousePosition.y >= rowY + ClipH) continue;
                    if (ev.mousePosition.x < barX  || ev.mousePosition.x > barX + barW)  continue;

                    // 기존 Notify 위에서 우클릭이면 → 잠금/복사/삭제 메뉴 (가장 가까운 마커 기준)
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
                        nmenu.AddItem(new GUIContent("Copy Notify"), false, () =>
                        {
                            _notifyClipboard = CloneNotify(
                                _config.Clips[capHitI].Notifies[capHitNi]);
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
                    if (_notifyClipboard != null)
                    {
                        menu.AddItem(
                            new GUIContent($"Paste {_notifyClipboard.Type} Notify"),
                            false, () => PasteNotify(capI, capN));
                    }
                    else
                    {
                        menu.AddDisabledItem(new GUIContent("Paste Notify"));
                    }
                    menu.AddSeparator("");
                    foreach (NotifyType nt in Enum.GetValues(typeof(NotifyType)))
                    {
                        var capType = nt;
                        menu.AddItem(new GUIContent($"Add {nt} Notify"), false, () =>
                        {
                            Undo.RecordObject(_config, "Add Notify");
                            var notify = new TrackNotify
                            {
                                Type = capType,
                                NormalizedTime = capN,
                            };
                            if (capType == NotifyType.Custom)
                                notify.ConfigEvent =
                                    ConfigEventType.HitShake;
                            _config.Clips[capI].Notifies.Add(notify);
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

        private bool TrySelectCameraPathPoint(Vector2 mousePosition)
        {
            if (_config == null
                || _notifyClipIdx < 0
                || _notifyClipIdx >= _config.Clips.Count)
                return false;

            TrackClip clip = _config.Clips[_notifyClipIdx];
            if (_selectedNotify < 0
                || _selectedNotify >= clip.Notifies.Count)
                return false;

            TrackNotify notify = clip.Notifies[_selectedNotify];
            if (notify.Payload is not CameraNotifyPayload payload
                || payload.Mode != CameraNotifyMode.Path)
                return false;

            float rowY = ClipRowTop(_notifyClipIdx) - _scrollY;
            float markerY = rowY + ClipH - 33f;
            for (int i = 0; i < payload.PathPoints.Count; i++)
            {
                float pointTime = GetCameraPathPointTrackTime(
                    clip, _notifyClipIdx, notify, payload, i);
                float pointX = LabelW + pointTime * _pxPerSec - _scrollX;
                var hitRect = new Rect(pointX - 10f, markerY - 1f, 20f, 15f);
                if (!hitRect.Contains(mousePosition)) continue;

                _selectedCameraPathPoint = i;
                _draggingCameraPathPointTime = true;
                _dragCameraPathPointIndex = i;
                MoveSceneViewToCameraPathPoint(payload, i);
                SceneView.RepaintAll();
                return true;
            }
            return false;
        }

        private void DragCameraPathPointTime(float mouseX)
        {
            if (_config == null
                || _notifyClipIdx < 0
                || _notifyClipIdx >= _config.Clips.Count)
                return;

            TrackClip clip = _config.Clips[_notifyClipIdx];
            if (_selectedNotify < 0
                || _selectedNotify >= clip.Notifies.Count)
                return;

            TrackNotify notify = clip.Notifies[_selectedNotify];
            if (notify.Payload is not CameraNotifyPayload payload
                || payload.Mode != CameraNotifyMode.Path
                || _dragCameraPathPointIndex < 0
                || _dragCameraPathPointIndex >= payload.PathPoints.Count
                || payload.PathMoveDuration <= 0f)
                return;

            float pointerTime =
                (mouseX - LabelW + _scrollX) / _pxPerSec;
            float moveStartTime = GetCameraNotifyTrackTime(
                clip, _notifyClipIdx, notify) + payload.PathBlendIn;
            float moveNormalizedTime = Mathf.Clamp01(
                (pointerTime - moveStartTime) / payload.PathMoveDuration);
            float pathTime = payload.PathMoveCurve != null
                ? Mathf.Clamp01(
                    payload.PathMoveCurve.Evaluate(moveNormalizedTime))
                : moveNormalizedTime;

            Undo.RecordObject(_config, "Move Camera Path Point Time");
            _dragCameraPathPointIndex = payload.SetPathPointTime(
                _dragCameraPathPointIndex, pathTime);
            _selectedCameraPathPoint = _dragCameraPathPointIndex;
            EditorUtility.SetDirty(_config);
            MoveSceneViewToCameraPathPoint(
                payload, _dragCameraPathPointIndex);
        }

        private void PasteNotify(int clipIndex, float normalizedTime)
        {
            if (_notifyClipboard == null
                || clipIndex < 0 || clipIndex >= _config.Clips.Count) return;

            Undo.RecordObject(_config, "Paste Notify");
            TrackNotify pasted = CloneNotify(_notifyClipboard);
            float intervalLength = Mathf.Max(0f,
                pasted.EndNormalizedTime - pasted.NormalizedTime);
            pasted.NormalizedTime = Mathf.Clamp01(normalizedTime);
            pasted.EndNormalizedTime = intervalLength > 0f
                ? Mathf.Clamp01(pasted.NormalizedTime + intervalLength)
                : 0f;

            List<TrackNotify> notifies = _config.Clips[clipIndex].Notifies;
            notifies.Add(pasted);
            _selectedClip = clipIndex;
            _selectedNotify = notifies.Count - 1;
            _notifyClipIdx = clipIndex;
            _fxDirty = true;
            EditorUtility.SetDirty(_config);
            _serializedConfig = new SerializedObject(_config);
            Repaint();
        }

        private static TrackNotify CloneNotify(TrackNotify source)
        {
            var clone = new TrackNotify
            {
                Type = source.Type,
                NormalizedTime = source.NormalizedTime,
                EndNormalizedTime = source.EndNormalizedTime,
                Locked = source.Locked,
            };

            switch (source.Payload)
            {
                case HitNotifyPayload hitPayload:
                    clone.Hit = source.Hit != null ? new HitData(source.Hit) : null;
                    if (clone.Payload is HitNotifyPayload clonedHit)
                    {
                        clonedHit.SyncWithEffect = hitPayload.SyncWithEffect;
                        clonedHit.Action = hitPayload.Action;
                        clonedHit.WarningDuration = hitPayload.WarningDuration;
                    }
                    break;
                case EffectNotifyPayload:
                    clone.Effect = source.Effect;
                    clone.Hit = source.Hit != null ? new HitData(source.Hit) : null;
                    clone.TransitionMode = source.TransitionMode;
                    clone.NextSection = source.NextSection;
                    break;
                case CameraNotifyPayload cameraPayload:
                    if (clone.Payload is CameraNotifyPayload clonedCamera)
                    {
                        clonedCamera.Mode = cameraPayload.Mode;
                        clonedCamera.Duration = cameraPayload.Duration;
                        clonedCamera.PositionAmplitude = cameraPayload.PositionAmplitude;
                        clonedCamera.RotationAmplitude = cameraPayload.RotationAmplitude;
                        clonedCamera.Frequency = cameraPayload.Frequency;
                        AnimationCurve envelope = cameraPayload.Envelope;
                        clonedCamera.Envelope = envelope == null
                            ? null
                            : new AnimationCurve(envelope.keys)
                            {
                                preWrapMode = envelope.preWrapMode,
                                postWrapMode = envelope.postWrapMode,
                            };
                        clonedCamera.ShotPosition = cameraPayload.ShotPosition;
                        clonedCamera.ShotEulerAngles =
                            cameraPayload.ShotEulerAngles;
                        clonedCamera.ShotFieldOfView =
                            cameraPayload.ShotFieldOfView;
                        clonedCamera.ShotEndPosition =
                            cameraPayload.ShotEndPosition;
                        clonedCamera.ShotEndEulerAngles =
                            cameraPayload.ShotEndEulerAngles;
                        clonedCamera.ShotEndFieldOfView =
                            cameraPayload.ShotEndFieldOfView;
                        clonedCamera.ShotBlendIn = cameraPayload.ShotBlendIn;
                        clonedCamera.ShotMoveDuration =
                            cameraPayload.ShotMoveDuration;
                        clonedCamera.ShotHold = cameraPayload.ShotHold;
                        clonedCamera.ShotBlendOut = cameraPayload.ShotBlendOut;
                        clonedCamera.ShotReturnBehindTarget =
                            cameraPayload.ShotReturnBehindTarget;
                        AnimationCurve shotCurve = cameraPayload.ShotBlendCurve;
                        clonedCamera.ShotBlendCurve = shotCurve == null
                            ? null
                            : new AnimationCurve(shotCurve.keys)
                            {
                                preWrapMode = shotCurve.preWrapMode,
                                postWrapMode = shotCurve.postWrapMode,
                            };
                        AnimationCurve moveCurve = cameraPayload.ShotMoveCurve;
                        clonedCamera.ShotMoveCurve = moveCurve == null
                            ? null
                            : new AnimationCurve(moveCurve.keys)
                            {
                                preWrapMode = moveCurve.preWrapMode,
                                postWrapMode = moveCurve.postWrapMode,
                            };
                        int pathPointCount = cameraPayload.PathPoints.Count;
                        var pathPointTimes = new float[pathPointCount];
                        var pathLookAtHeights = new float[pathPointCount];
                        for (int i = 0; i < pathPointCount; i++)
                        {
                            pathPointTimes[i] =
                                cameraPayload.GetPathPointTime(i);
                            pathLookAtHeights[i] =
                                cameraPayload.GetPathPointLookAtHeight(i);
                        }
                        clonedCamera.SetPathPointData(
                            cameraPayload.PathPoints,
                            pathPointTimes,
                            pathLookAtHeights);
                        clonedCamera.PathStartLookAtHeight =
                            cameraPayload.PathStartLookAtHeight;
                        clonedCamera.PathEndLookAtHeight =
                            cameraPayload.PathEndLookAtHeight;
                        clonedCamera.PathStartFieldOfView =
                            cameraPayload.PathStartFieldOfView;
                        clonedCamera.PathEndFieldOfView =
                            cameraPayload.PathEndFieldOfView;
                        clonedCamera.PathBlendIn = cameraPayload.PathBlendIn;
                        clonedCamera.PathMoveDuration =
                            cameraPayload.PathMoveDuration;
                        clonedCamera.PathHold = cameraPayload.PathHold;
                        clonedCamera.PathBlendOut = cameraPayload.PathBlendOut;
                        clonedCamera.PathReturnBehindTarget =
                            cameraPayload.PathReturnBehindTarget;
                        AnimationCurve pathBlendCurve =
                            cameraPayload.PathBlendCurve;
                        clonedCamera.PathBlendCurve = pathBlendCurve == null
                            ? null
                            : new AnimationCurve(pathBlendCurve.keys)
                            {
                                preWrapMode = pathBlendCurve.preWrapMode,
                                postWrapMode = pathBlendCurve.postWrapMode,
                            };
                        AnimationCurve pathMoveCurve =
                            cameraPayload.PathMoveCurve;
                        clonedCamera.PathMoveCurve = pathMoveCurve == null
                            ? null
                            : new AnimationCurve(pathMoveCurve.keys)
                            {
                                preWrapMode = pathMoveCurve.preWrapMode,
                                postWrapMode = pathMoveCurve.postWrapMode,
                            };
                    }
                    break;
                case SoundNotifyPayload soundPayload:
                    if (clone.Payload is SoundNotifyPayload clonedSound)
                    {
                        clonedSound.Sound = soundPayload.Sound;
                        clonedSound.Loop = soundPayload.Loop;
                        clonedSound.NextSection = soundPayload.NextSection;
                        SoundFadeModule fadeModule =
                            soundPayload.FindModule<SoundFadeModule>();
                        if (fadeModule != null)
                            clonedSound.Modules.Add(new SoundFadeModule(
                                fadeModule.FadeInDuration,
                                fadeModule.FadeOutDuration));
                        SoundDurationModule durationModule =
                            soundPayload.FindModule<SoundDurationModule>();
                        if (durationModule != null)
                            clonedSound.Modules.Add(new SoundDurationModule(
                                durationModule.Duration));
                    }
                    break;
                case CustomNotifyPayload customPayload:
                    clone.ConfigEvent = customPayload.EventType;
                    break;
            }

            return clone;
        }

        private void FocusClipInTimeline(int clipIndex, float viewWidth)
        {
            if (clipIndex < 0 || clipIndex >= _config.Clips.Count || viewWidth <= 0f) return;

            const float leftPadding = 12f;
            float clipStartX = GetClipStartTime(clipIndex) * _pxPerSec;
            float contentWidth = GetTotalDuration() * _pxPerSec + 40f;
            float maxScrollX = Mathf.Max(0f, contentWidth - viewWidth);
            _scrollX = Mathf.Clamp(clipStartX - leftPadding, 0f, maxScrollX);
        }

        private bool TryBeginModuleWindowDrag(Vector2 mousePosition)
        {
            const float handleRadius = 6f;

            for (int clipIndex = 0; clipIndex < _config.Clips.Count; clipIndex++)
            {
                TrackClip tc = _config.Clips[clipIndex];
                if (!ModulesExpanded(tc) || tc.Clip == null) continue;

                float lanesY = ClipRowTop(clipIndex) - _scrollY + ClipH;
                float duration = tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
                float barX = LabelW + GetClipStartTime(clipIndex) * _pxPerSec - _scrollX;
                float barW = duration * _pxPerSec;

                for (int moduleIndex = 0; moduleIndex < tc.Modules.Count; moduleIndex++)
                {
                    if (!(tc.Modules[moduleIndex] is WindowModule window)) continue;

                    float laneY = lanesY + moduleIndex * ModuleLaneH;
                    if (mousePosition.y < laneY || mousePosition.y >= laneY + ModuleLaneH) continue;

                    float start = Mathf.Clamp01(window.Start);
                    float end = Mathf.Clamp01(window.End);
                    float startX = barX + start * barW;
                    float endX = barX + end * barW;
                    float startDistance = Mathf.Abs(mousePosition.x - startX);
                    float endDistance = Mathf.Abs(mousePosition.x - endX);

                    if (startDistance > handleRadius && endDistance > handleRadius) return false;

                    _dragModuleClip = tc;
                    _dragWindowModule = window;
                    // 겹친 핸들은 End를 우선해 [0,0] 진입 모듈을 오른쪽으로 펼칠 수 있게 한다.
                    _dragWindowStart = startDistance < endDistance;
                    _selectedClip = clipIndex;
                    _selectedNotify = -1;
                    Undo.RecordObject(_config, "Edit Module Window");
                    return true;
                }
            }
            return false;
        }

        private void DragModuleWindow(float mouseX)
        {
            int clipIndex = _config.Clips.IndexOf(_dragModuleClip);
            if (clipIndex < 0 || _dragModuleClip.Clip == null) return;

            float duration = _dragModuleClip.Clip.length
                / Mathf.Max(0.01f, _dragModuleClip.Speed);
            float barX = LabelW + GetClipStartTime(clipIndex) * _pxPerSec - _scrollX;
            float barW = duration * _pxPerSec;
            if (barW <= 0f) return;

            float normalized = Mathf.Clamp01((mouseX - barX) / barW);
            normalized = SnapModuleTimeToFrame(_dragModuleClip, normalized);

            if (_dragWindowStart)
                _dragWindowModule.Start = Mathf.Min(normalized, _dragWindowModule.End);
            else
                _dragWindowModule.End = Mathf.Max(normalized, _dragWindowModule.Start);

            EditorUtility.SetDirty(_config);
        }

        private static float SnapModuleTimeToFrame(TrackClip tc, float normalized)
        {
            if (tc.Clip == null || tc.Clip.frameRate <= 0f) return normalized;
            float frameCount = tc.Clip.length * tc.Clip.frameRate;
            if (frameCount <= 0f) return normalized;
            return Mathf.Clamp01(Mathf.Round(normalized * frameCount) / frameCount);
        }
    }
}
