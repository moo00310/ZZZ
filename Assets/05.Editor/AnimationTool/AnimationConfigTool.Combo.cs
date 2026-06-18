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
        // 링크 흐름 재생: 현재 클립을 재생하다 윈도우 안에서 입력이 들어오면 타겟으로 점프
        private void ComboUpdate(float delta)
        {
            if (_comboActiveClip < 0 || _comboActiveClip >= _config.Clips.Count)
            { _isPlaying = false; return; }

            var tc = _config.Clips[_comboActiveClip];
            if (tc.Clip == null) { _isPlaying = false; return; }

            float clipLen = tc.Clip.length;
            _comboClipTime += delta * Mathf.Max(0.01f, tc.Speed);
            float nt = clipLen > 0f ? _comboClipTime / clipLen : 1f;

            // 블렌딩 진행
            if (_blending)
            {
                _blendElapsed += delta;
                if (_blendElapsed >= _blendDuration) _blending = false;
            }

            // 클립 고유 링크 먼저, 그 다음 config 공통 링크(Global) 검사 (런타임 ConfigState와 동일)
            if (TryLinksPreview(tc.Links, tc, nt)) return;
            if (_config.GlobalLinks != null && TryLinksPreview(_config.GlobalLinks, tc, nt)) return;

            // 클립 끝 도달 (루프 클립은 계속 반복)
            if (nt >= 1f)
            {
                if (tc.IsLooping) { _comboClipTime = Mathf.Repeat(_comboClipTime, clipLen); }
                else if (_config.LoopTrack)
                {
                    _comboLog += "  → [Loop]";
                    RestartCombo();
                    return;
                }
                else
                {
                    _comboLog += "  → [End]";
                    _isPlaying = false;
                    _comboClipTime = clipLen;
                }
            }

            float ct = Mathf.Clamp(_comboClipTime, 0f, clipLen);
            if (_blending && _blendFromClip != null && _blendDuration > 0.0001f)
            {
                float w = Mathf.Clamp01(_blendElapsed / _blendDuration);
                SampleBlended(_blendFromClip, _blendFromTime, tc.Clip, ct, w);
                // 루트모션 클립이면 블렌드 중에도 루트본을 0으로 → 베이크 이동량 튐 방지
                if (tc.UseRootMotion || _blendFromUsesRM) ResetRootBoneVisual();
            }
            else
            {
                SampleClipPose(tc, _comboActiveClip, ct, true);
            }
        }

        // 이전 클립(from) 포즈와 새 클립(to) 포즈를 본 단위로 보간 (CrossFade 시뮬)
        private void SampleBlended(AnimationClip fromClip, float fromTime,
            AnimationClip toClip, float toTime, float w)
        {
            if (_target == null) return;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            CachePoseBones();

            // A 포즈 (이전 클립) 캡처
            AnimationMode.SampleAnimationClip(_target, fromClip, fromTime);
            for (int i = 0; i < _poseBones.Length; i++)
            {
                _poseAPos[i] = _poseBones[i].localPosition;
                _poseARot[i] = _poseBones[i].localRotation;
            }

            // B 포즈 (새 클립) → A에서 B로 w만큼 보간
            AnimationMode.SampleAnimationClip(_target, toClip, toTime);
            for (int i = 0; i < _poseBones.Length; i++)
            {
                _poseBones[i].localPosition =
                    Vector3.Lerp(_poseAPos[i], _poseBones[i].localPosition, w);
                _poseBones[i].localRotation =
                    Quaternion.Slerp(_poseARot[i], _poseBones[i].localRotation, w);
            }
            SceneView.RepaintAll();
        }

        private void CachePoseBones()
        {
            if (_poseBones != null && _poseBones.Length > 0 &&
                _poseBones[0] != null) return;
            _poseBones = _target.GetComponentsInChildren<Transform>(true);
            _poseAPos  = new Vector3[_poseBones.Length];
            _poseARot  = new Quaternion[_poseBones.Length];
        }

        // 콤보를 Entry 섹션으로 되돌리고 위치를 원점으로 복귀
        private void RestartCombo()
        {
            int entry = _config.IndexOfSection(_config.EntrySection);
            _comboActiveClip = entry >= 0 ? entry : 0;
            _comboClipTime   = 0f;
            _rmTracker.Reset();
            _blending        = false;
            if (_target != null) _target.transform.position = _targetOriginPos;
            _comboLog += $" → {SectionLabel(_comboActiveClip)}";
        }

        // links를 순서대로 검사해 첫 발동 링크로 점프. 점프했으면 true.
        private bool TryLinksPreview(List<ClipLink> links, TrackClip tc, float nt)
        {
            foreach (var link in links)
            {
                if (!ConditionMatches(link)) continue;

                float p = tc.IsLooping ? Mathf.Repeat(nt, 1f) : nt;
                bool fire = false;
                switch (link.Timing)
                {
                    case LinkTiming.WhenMatched:  fire = p >= link.WindowStart && p <= link.WindowEnd; break;
                    case LinkTiming.OnWindowMiss: fire = p > link.WindowEnd;                            break;
                    case LinkTiming.OnEnd:        fire = p >= EndThreshold(tc);                         break;
                }

                if (fire) { JumpToLink(link); return true; }
            }
            return false;
        }

        // 링크의 공격+방향 조건이 현재 시뮬레이션 입력 상태와 모두 맞는지
        private bool ConditionMatches(ClipLink link)
            => AttackMatches(link.Attack) && MoveMatches(link.Direction);

        // OnEnd 발동 기준 (런타임 ConfigState와 동일 규칙)
        private float EndThreshold(TrackClip tc)
        {
            float dt = _config != null ? _config.DoneThreshold : 0f;
            if (dt > 0f && dt < 1f) return dt;
            if (tc.Clip != null && tc.Clip.frameRate > 0f)
            {
                float frames = tc.Clip.length * tc.Clip.frameRate;
                if (frames > 1f) return Mathf.Clamp01(1f - 1f / frames);
            }
            return 0.999f;
        }

        // 공격 입력 조건 (눌러둔 토글 기준)
        private bool AttackMatches(ComboInput required)
        {
            switch (required)
            {
                case ComboInput.None: return !AnyInputHeld();
                case ComboInput.Any:  return AnyInputHeld();
                default:              return _heldInput[(int)required];
            }
        }

        private bool AnyInputHeld()
        {
            for (int i = 0; i < _heldInput.Length; i++)
                if (_heldInput[i]) return true;
            return false;
        }

        // 링크의 이동 조건이 현재 시뮬레이션 방향과 맞는지
        private bool MoveMatches(MoveDir req)
        {
            switch (req)
            {
                case MoveDir.Any:    return true;
                case MoveDir.Moving: return _simMoveDir != MoveDir.Neutral;
                default:             return req == _simMoveDir;
            }
        }

        private void JumpToLink(ClipLink link)
        {
            // 블렌드용으로 현재(이전) 클립 먼저 캡처
            var fromTc = _config.Clips[_comboActiveClip];

            // ── 다른 config로 전이 → 프리뷰 config 자체를 교체 ──
            if (link.TargetConfig != null && link.TargetConfig != _config)
            {
                var newCfg = link.TargetConfig;
                if (newCfg.Clips.Count == 0) { _isPlaying = false; return; }

                int t = !string.IsNullOrEmpty(link.TargetSection)
                    ? newCfg.IndexOfSection(link.TargetSection)
                    : newCfg.IndexOfSection(newCfg.EntrySection);
                if (t < 0) t = 0;

                // 표시 중인 config 교체
                _config           = newCfg;
                _serializedConfig = new SerializedObject(_config);
                _selectedClip     = -1;
                _selectedNotify   = -1;
                _scrollX          = 0f;
                _scrollY          = 0f;

                _comboLog += $"  →[{newCfg.name}] {SectionLabel(t)}";
                BeginJump(fromTc, t, link.BlendDuration);
                return;
            }

            // ── 같은 config 내 전이 ──
            int ti = _config.IndexOfSection(link.TargetSection);
            if (ti < 0)   // End / Loop
            {
                if (_config.LoopTrack) { _comboLog += "  → [Loop]"; RestartCombo(); }
                else                   { _comboLog += "  → [End]";  _isPlaying = false; }
                return;
            }

            _comboLog += $"  → {SectionLabel(ti)}";
            BeginJump(fromTc, ti, link.BlendDuration);
        }

        // 이전 클립 → toIdx 클립으로 전이 (블렌드 + 루트모션 추적 초기화)
        private void BeginJump(TrackClip fromTc, int toIdx, float blendDur)
        {
            if (fromTc.Clip != null && blendDur > 0.0001f)
            {
                _blending        = true;
                _blendFromClip   = fromTc.Clip;
                _blendFromUsesRM = fromTc.UseRootMotion;
                _blendFromTime   = Mathf.Clamp(_comboClipTime, 0f, fromTc.Clip.length);
                _blendElapsed    = 0f;
                _blendDuration   = blendDur;
            }
            else _blending = false;

            _comboActiveClip = toIdx;
            _comboClipTime   = 0f;
            _rmTracker.Reset();
        }

        private string SectionLabel(int idx)
        {
            var c = _config.Clips[idx];
            return Short(!string.IsNullOrEmpty(c.SectionName) ? c.SectionName
                 : c.Clip != null ? c.Clip.name : $"Clip{idx}");
        }
    }
}
