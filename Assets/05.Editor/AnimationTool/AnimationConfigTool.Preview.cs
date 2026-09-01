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
                    ResetRootMotionPreview();
                    RestorePreviewOrigin();
                }
                else { _trackTime = total; _isPlaying = false; }
            }
            SampleAtTime(_trackTime, true);
        }
        // ── Preview 제어 ─────────────────────────────────────────
        private void StartPreview()
        {
            if (_target == null || _config == null || _config.Clips.Count == 0) return;
            CachePreviewRig();
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            _isPlaying       = true;
            _lastEditorTime  = EditorApplication.timeSinceStartup;
            ResetRootMotionPreview();
            _blending        = false;
            _targetOriginPos = _target.transform.position;
            _targetOriginRotation = _target.transform.rotation;

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
            ApplyPreviewSectionTurnFromBones(tc, clipIdx, clipTime, advancePlayback);
            SuppressPreviewBip001HorizontalMotion();
            SceneView.RepaintAll();
        }

        // AnimationClip의 RootT/RootQ를 런타임 OnAnimatorMove와 같은 방식으로 적용한다.
        private void ApplyRootMotion(TrackClip tc, int clipIdx, float clipTime, bool advancePlayback)
        {
            if (!tc.UseRootMotion || !advancePlayback) return;

            if (TryEvaluateRootPosition(tc.Clip, clipTime, out Vector3 rootPosition))
            {
                Vector3 deltaLocal = _rmTracker.NextDelta(
                    clipIdx, clipTime, rootPosition, tc.IsLooping);
                deltaLocal.y = 0f;
                ApplyPreviewBackMotionScale(tc, ref deltaLocal);
                _target.transform.position +=
                    _target.transform.TransformDirection(deltaLocal);
            }

            if (!TryEvaluateRootRotation(tc.Clip, clipTime,
                    out Quaternion rootRotation)) return;

            bool boundary = _rootRotationClip != clipIdx
                || clipTime + 0.0001f < _rootRotationTime;
            Quaternion deltaRotation = boundary
                ? Quaternion.identity
                : rootRotation * Quaternion.Inverse(_previousRootRotation);

            _rootRotationClip = clipIdx;
            _rootRotationTime = clipTime;
            _previousRootRotation = rootRotation;
            if (boundary) _rootRotationApplied = 0f;

            ApplyPreviewRootRotation(tc, deltaRotation);
        }

        private void ApplyPreviewBackMotionScale(TrackClip tc, ref Vector3 deltaLocal)
        {
            var module = FindModule<BackMotionScaleModule>(tc);
            if (module == null || deltaLocal.z >= 0f) return;
            deltaLocal.z *= module.Scale;
        }

        private void ApplyPreviewRootRotation(TrackClip tc, Quaternion deltaRotation)
        {
            if (FindModule<RootRotationKillModule>(tc) != null
                || FindModule<FaceViewModule>(tc) != null)
                return;

            if (FindModule<SectionTurnModule>(tc) != null) return;

            float deltaYaw = RootTurnAngle(
                deltaRotation, RootMotionRotationAxis.Auto);

            if (Mathf.Abs(deltaYaw) > 1e-5f)
                _target.transform.rotation = Quaternion.AngleAxis(deltaYaw, Vector3.up)
                    * _target.transform.rotation;
        }

        private void ApplyPreviewSectionTurnFromBones(TrackClip tc, int clipIdx,
            float clipTime, bool advancePlayback)
        {
            var turn = FindModule<SectionTurnModule>(tc);
            if (!advancePlayback || turn == null
                || FindModule<RootRotationKillModule>(tc) != null
                || FindModule<FaceViewModule>(tc) != null
                || _previewBip001Bone == null || _previewRootBone == null)
            {
                _hasPreviewSectionTurnAngle = false;
                return;
            }

            Quaternion bip001Rotation = _previewBip001Bone.localRotation;
            Quaternion rootRotation = _previewRootBone.localRotation;
            bool boundary = !_hasPreviewSectionTurnAngle
                || _previewSectionTurnClip != clipIdx
                || clipTime + 0.0001f < _previewSectionTurnTime;

            _previewSectionTurnClip = clipIdx;
            _previewSectionTurnTime = clipTime;
            if (boundary)
            {
                _previousPreviewSectionTurnBip001Rotation = bip001Rotation;
                _previousPreviewSectionTurnRootRotation = rootRotation;
                _previewSectionTurnBip001BaselineRotation = bip001Rotation;
                _hasPreviewSectionTurnAngle = true;
                _rootRotationApplied = 0f;
                CounterRotatePreviewSectionTurnBone(
                    turn.SourceAxis, bip001Rotation);
                return;
            }

            Quaternion bip001FrameDelta = bip001Rotation
                * Quaternion.Inverse(_previousPreviewSectionTurnBip001Rotation);
            Quaternion rootFrameDelta = rootRotation
                * Quaternion.Inverse(_previousPreviewSectionTurnRootRotation);
            float bip001Delta = RootTurnAngle(bip001FrameDelta, turn.SourceAxis);
            float rootDelta = RootTurnAngle(rootFrameDelta, turn.SourceAxis);
            _previousPreviewSectionTurnBip001Rotation = bip001Rotation;
            _previousPreviewSectionTurnRootRotation = rootRotation;

            float normalizedTime = tc.Clip.length > 0f
                ? clipTime / tc.Clip.length
                : 0f;
            if (normalizedTime >= turn.Start && normalizedTime <= turn.End)
            {
                float deltaYaw = (bip001Delta - rootDelta)
                    * Mathf.Max(0f, turn.RotationScale);
                if (turn.TargetAngle > 0f)
                {
                    float remaining = Mathf.Max(0f,
                        turn.TargetAngle - Mathf.Abs(_rootRotationApplied));
                    deltaYaw = Mathf.Clamp(deltaYaw, -remaining, remaining);
                }

                _rootRotationApplied += deltaYaw;
                if (Mathf.Abs(deltaYaw) > 1e-5f)
                    _target.transform.rotation = Quaternion.AngleAxis(deltaYaw, Vector3.up)
                        * _target.transform.rotation;
            }

            CounterRotatePreviewSectionTurnBone(
                turn.SourceAxis, bip001Rotation);
        }

        private void CounterRotatePreviewSectionTurnBone(
            RootMotionRotationAxis sourceAxis,
            Quaternion currentBip001Rotation)
        {
            Quaternion rotationFromBaseline = currentBip001Rotation
                * Quaternion.Inverse(_previewSectionTurnBip001BaselineRotation);
            float turnAngle = RootTurnAngle(rotationFromBaseline, sourceAxis);
            if (Mathf.Abs(turnAngle) <= 1e-5f) return;

            _previewBip001Bone.localRotation = Quaternion.AngleAxis(
                -turnAngle, RootTurnAxis(sourceAxis))
                * currentBip001Rotation;
        }

        private static T FindModule<T>(TrackClip tc) where T : SectionModule
        {
            if (tc.Modules == null) return null;
            for (int i = 0; i < tc.Modules.Count; i++)
                if (tc.Modules[i] is T module) return module;
            return null;
        }

        private static bool TryEvaluateRootPosition(AnimationClip clip, float time,
            out Vector3 position)
        {
            AnimationCurve x = RootCurve(clip, "RootT.x");
            AnimationCurve y = RootCurve(clip, "RootT.y");
            AnimationCurve z = RootCurve(clip, "RootT.z");
            bool found = x != null || y != null || z != null;
            position = new Vector3(
                x != null ? x.Evaluate(time) : 0f,
                y != null ? y.Evaluate(time) : 0f,
                z != null ? z.Evaluate(time) : 0f);
            return found;
        }

        private static bool TryEvaluateRootRotation(AnimationClip clip, float time,
            out Quaternion rotation)
        {
            AnimationCurve x = RootCurve(clip, "RootQ.x");
            AnimationCurve y = RootCurve(clip, "RootQ.y");
            AnimationCurve z = RootCurve(clip, "RootQ.z");
            AnimationCurve w = RootCurve(clip, "RootQ.w");
            bool found = x != null || y != null || z != null || w != null;
            rotation = new Quaternion(
                x != null ? x.Evaluate(time) : 0f,
                y != null ? y.Evaluate(time) : 0f,
                z != null ? z.Evaluate(time) : 0f,
                w != null ? w.Evaluate(time) : 1f);

            float magnitude = Mathf.Sqrt(rotation.x * rotation.x
                + rotation.y * rotation.y
                + rotation.z * rotation.z
                + rotation.w * rotation.w);
            if (magnitude > 1e-6f)
            {
                float inverse = 1f / magnitude;
                rotation = new Quaternion(rotation.x * inverse, rotation.y * inverse,
                    rotation.z * inverse, rotation.w * inverse);
            }
            else
            {
                rotation = Quaternion.identity;
            }
            return found;
        }

        private static AnimationCurve RootCurve(AnimationClip clip, string propertyName)
            => AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), propertyName));

        private static float RootTurnAngle(Quaternion rotation,
            RootMotionRotationAxis sourceAxis)
        {
            float x = rotation.x;
            float y = rotation.y;
            float z = rotation.z;
            float w = rotation.w;
            if (w < 0f)
            {
                x = -x;
                y = -y;
                z = -z;
                w = -w;
            }

            float component;
            switch (sourceAxis)
            {
                case RootMotionRotationAxis.X:
                    component = x;
                    break;
                case RootMotionRotationAxis.Y:
                    component = y;
                    break;
                case RootMotionRotationAxis.Z:
                    component = z;
                    break;
                default:
                    float absX = Mathf.Abs(x);
                    float absY = Mathf.Abs(y);
                    float absZ = Mathf.Abs(z);
                    component = absX >= absY && absX >= absZ
                        ? x
                        : absY >= absZ ? y : z;
                    break;
            }

            if (component * component + w * w < 1e-12f) return 0f;
            return 2f * Mathf.Atan2(component, w) * Mathf.Rad2Deg;
        }

        private static Vector3 RootTurnAxis(RootMotionRotationAxis sourceAxis)
        {
            switch (sourceAxis)
            {
                case RootMotionRotationAxis.X:
                    return Vector3.right;
                case RootMotionRotationAxis.Z:
                    return Vector3.forward;
                default:
                    return Vector3.up;
            }
        }

        private void ResetRootMotionPreview()
        {
            _rmTracker.Reset();
            _rootRotationClip = -1;
            _rootRotationTime = 0f;
            _rootRotationApplied = 0f;
            _previousRootRotation = Quaternion.identity;
            _previewSectionTurnClip = -1;
            _previewSectionTurnTime = 0f;
            _previousPreviewSectionTurnBip001Rotation = Quaternion.identity;
            _previousPreviewSectionTurnRootRotation = Quaternion.identity;
            _previewSectionTurnBip001BaselineRotation = Quaternion.identity;
            _hasPreviewSectionTurnAngle = false;
        }

        private void RestorePreviewOrigin()
        {
            if (_target == null) return;
            _target.transform.SetPositionAndRotation(
                _targetOriginPos, _targetOriginRotation);
        }

        private void CachePreviewRig()
        {
            _previewBip001Bone = null;
            _previewRootBone = null;
            if (_target == null) return;

            Transform[] bones = _target.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i].name == "Bip001")
                    _previewBip001Bone = bones[i];
                else if (bones[i].name == "Root")
                    _previewRootBone = bones[i];

                if (_previewBip001Bone != null && _previewRootBone != null) break;
            }
        }

        private void SuppressPreviewBip001HorizontalMotion()
        {
            if (_previewBip001Bone == null) return;

            Vector3 localPosition = _previewBip001Bone.localPosition;
            localPosition.x = 0f;
            localPosition.z = 0f;
            _previewBip001Bone.localPosition = localPosition;
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
            ResetRootMotionPreview();
            _blending        = false;
            _comboLog        = "";
            if (_target != null && _config != null)
            {
                RestorePreviewOrigin();
                if (_comboMode) { /* 포즈는 StartPreview에서 */ }
                else SampleAtTime(0f, false);
            }
        }
    }
}
