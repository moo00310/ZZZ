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
        private void SequentialUpdate(float delta)
        {
            _trackTime += delta;
            float total = GetTotalDuration();
            if (_trackTime >= total)
            {
                if (_config.LoopTrack)
                {
                    // 처음으로 되돌리고 위치를 원점(시작 위치)으로 복귀
                    _trackTime     = 0f;
                    _rmTracker.Reset();
                    if (_target != null) _target.transform.position = _targetOriginPos;
                }
                else { _trackTime = total; _isPlaying = false; }
            }
            SampleAtTime(_trackTime, true);
        }
        // ── Preview 제어 ─────────────────────────────────────────
        private void StartPreview()
        {
            if (_target == null || _config == null || _config.Clips.Count == 0) return;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            _isPlaying       = true;
            _lastEditorTime  = EditorApplication.timeSinceStartup;
            _rmTracker.Reset();
            _blending        = false;
            _targetOriginPos = _target.transform.position;

            if (_comboMode)
            {
                // 시작 클립: 선택된 클립 > EntrySection > 첫 클립
                int start = (_selectedClip >= 0 && _selectedClip < _config.Clips.Count)
                    ? _selectedClip
                    : _config.IndexOfSection(_config.EntrySection);
                _comboActiveClip = start >= 0 ? start : 0;
                _comboClipTime   = 0f;
                _comboLog        = SectionLabel(_comboActiveClip);
                SampleClipPose(_config.Clips[_comboActiveClip], _comboActiveClip, 0f, false);
            }
            else
            {
                if (_trackTime >= GetTotalDuration()) _trackTime = 0f;
                SampleAtTime(_trackTime, false);
            }
        }

        private void StopPreview() => _isPlaying = false;

        private void ExitPreview()
        {
            _isPlaying = false;
            if (!AnimationMode.InAnimationMode()) return;

            // StopAnimationMode는 내부적으로 UI Toolkit 바인딩 스타일 갱신을 강제하는데,
            // 윈도우 종료 시점엔 다른 Inspector의 SerializedObject가 이미 Dispose돼 있어
            // "SerializedObject ... has been Disposed" NRE가 Unity 내부 폴러에서 발생한다.
            // 우리 코드 밖에서 나는 무해한 예외이므로 삼킨다 (Unity 알려진 이슈).
            try { AnimationMode.StopAnimationMode(); }
            catch (System.NullReferenceException) { }

            ClearFxPreview();   // 이펙트 프리뷰 인스턴스도 정리
        }

        private void SampleAtTime(float time, bool advancePlayback)
        {
            if (_target == null || _config == null) return;
            if (EditorApplication.isPlaying) return;   // 런타임 애니메이터와 충돌 방지
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();

            // Effect Notify 선택 시: 애니 포즈와 같은 시간축으로 이펙트 시뮬레이션(순차 모드 전용)
            if (EffectPreviewActive) UpdateEffectPreview(time);

            float t = 0f;
            for (int i = 0; i < _config.Clips.Count; i++)
            {
                var   tc  = _config.Clips[i];
                if (tc.Clip == null) continue;
                float dur = tc.Clip.length / Mathf.Max(0.01f, tc.Speed);

                if (time <= t + dur || i == _config.Clips.Count - 1)
                {
                    float local    = Mathf.Clamp(time - t, 0f, dur);
                    float clipTime = local * tc.Speed;
                    if (tc.IsLooping) clipTime = Mathf.Repeat(clipTime, tc.Clip.length);
                    clipTime = Mathf.Clamp(clipTime, 0f, tc.Clip.length);
                    SampleClipPose(tc, i, clipTime, advancePlayback);
                    return;
                }
                t += dur;
            }
        }

        // 단일 클립을 clipTime(초)에 포즈시키고 루트모션 적용
        private void SampleClipPose(TrackClip tc, int clipIdx, float clipTime, bool advancePlayback)
        {
            if (_target == null || tc.Clip == null) return;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            AnimationMode.SampleAnimationClip(_target, tc.Clip, clipTime);
            ApplyRootMotion(tc, clipIdx, clipTime, advancePlayback);
            SceneView.RepaintAll();
        }

        // PlayerController.LateUpdate와 동일: 루트본 localPosition 델타를 월드 이동으로 변환
        private void ApplyRootMotion(TrackClip tc, int clipIdx, float clipTime, bool advancePlayback)
        {
            if (!tc.UseRootMotion || _bip001Bone == null) return;

            if (advancePlayback)
            {
                // 순수 델타 판정은 트래커에 위임 (클립 전환/루프 wrap 시 0 반환).
                // 월드 변환 + 스케일만 여기서 — Transform을 아는 쪽 책임.
                Vector3 deltaLocal = _rmTracker.NextDelta(
                    clipIdx, clipTime, _bip001Bone.localPosition, tc.IsLooping);
                deltaLocal.y = 0f;   // 수평만 이동 — Y(수직 바운스)는 메시에 남긴다 (런타임과 동일)
                _target.transform.position +=
                    _target.transform.TransformDirection(deltaLocal) * _rootMotionScale;
            }

            ResetRootBoneVisual();
        }

        // 비주얼: Bip001 X·Z 리셋(Y 유지) → 베이크된 수평 이동량이 메시에 남는 것 방지 (런타임과 동일)
        private void ResetRootBoneVisual()
        {
            if (_bip001Bone == null) return;
            Vector3 lp = _bip001Bone.localPosition;
            lp.x = 0f; lp.z = 0f;
            _bip001Bone.localPosition = lp;
        }

        // target에 PlayerController가 있으면 _bip001Bone/_rootMotionScale 자동 추출
        private void AutoDetectRootBones()
        {
            _bip001Bone = null; _rootMotionScale = 1f;
            if (_target == null) return;

            var pc = _target.GetComponentInChildren<ZZZ.Player.PlayerController>();
            if (pc == null) return;

            var so = new SerializedObject(pc);
            var bb = so.FindProperty("_bip001Bone");
            var sc = so.FindProperty("_rootMotionScale");
            if (bb != null) _bip001Bone      = bb.objectReferenceValue as Transform;
            if (sc != null) _rootMotionScale = sc.floatValue;
        }

        // ── 시간 헬퍼 ────────────────────────────────────────────
        private float GetTotalDuration()
        {
            if (_config == null) return 0f;
            float t = 0f;
            foreach (var tc in _config.Clips)
                if (tc.Clip != null) t += tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
            return t;
        }

        private float GetClipStartTime(int idx)
        {
            if (_config == null) return 0f;
            float t = 0f;
            for (int i = 0; i < idx && i < _config.Clips.Count; i++)
            {
                var tc = _config.Clips[i];
                if (tc.Clip != null) t += tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
            }
            return t;
        }
        private void ResetPreview()
        {
            StopPreview();
            _trackTime       = 0f;
            _rmTracker.Reset();
            _blending        = false;
            _comboLog        = "";
            if (_target != null && _config != null)
            {
                _target.transform.position = _targetOriginPos;
                if (_comboMode) { /* 포즈는 StartPreview에서 */ }
                else SampleAtTime(0f, false);
            }
        }
    }
}
