using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ;
using ZZZ.Player.StateMachine;

namespace ZZZ.Editor.AnimationTool
{
    public class AnimationConfigTool : EditorWindow
    {
        // ── 기본 ─────────────────────────────────────────────────
        // [SerializeField] = Play 진입 시 도메인 리로드 후에도 선택 상태 유지
        [SerializeField] private GameObject      _target;
        [SerializeField] private AnimationConfig _config;
        private SerializedObject _serializedConfig;

        // ── Preview ───────────────────────────────────────────────
        private bool   _isPlaying;
        private float  _trackTime;
        private double _lastEditorTime;
        private float  _previewSpeed = 1f;

        // ── Combo Preview (링크 따라가기) ─────────────────────────
        private bool   _comboMode;        // true = 링크 흐름 재생
        private int    _comboActiveClip;  // 현재 재생 중 클립 인덱스
        private float  _comboClipTime;    // 현재 클립 로컬 시간(초, 스피드 적용 후)
        private bool[]  _heldInput = new bool[5]; // 토글로 눌러둔 입력 (ComboInput 인덱스)
        private MoveDir _simMoveDir = MoveDir.Neutral;  // 시뮬레이션 이동 방향
        private string  _comboLog = "";   // 전이 흐름 로그

        // ── Transition 블렌딩 (CrossFade 시뮬레이션) ──────────────
        private bool          _blending;
        private AnimationClip _blendFromClip;
        private bool          _blendFromUsesRM; // 이전 클립이 루트모션 사용 여부
        private float         _blendFromTime;   // 이전 클립 로컬 시간(초)
        private float         _blendElapsed;
        private float         _blendDuration;
        private Transform[]   _poseBones;        // 보간 대상 본 캐시
        private Vector3[]     _poseAPos;
        private Quaternion[]  _poseARot;

        // ── 레이아웃 상수 ─────────────────────────────────────────
        private const float ToolbarH  = 28f;
        private const float PlaybarH  = 58f;
        private const float RulerH    = 22f;
        private const float ClipH     = 58f;
        private const float ClipGap   = 3f;
        private const float LabelW    = 144f;
        private const float HScrollH  = 14f;    // 하단 가로 스크롤바 높이

        // ── 타임라인 ─────────────────────────────────────────────
        [SerializeField] private float _pxPerSec = 80f;
        [SerializeField] private float _scrollX  = 0f;   // 수평 스크롤 — 하단 스크롤바로 조정
        [SerializeField] private float _scrollY  = 0f;   // 수직 스크롤 — 마우스 휠로 조정

        // ── 선택 ─────────────────────────────────────────────────
        [SerializeField] private int _selectedClip   = -1;
        private int _selectedNotify = -1;
        private int _notifyClipIdx  = -1;
        private bool _showTrack;       // Track/Global Links 인스펙터 표시 여부 (상단 버튼으로만 켬)
        private bool _clipAdvFold;     // 클립 인스펙터의 고급(Boost/Tracking/Turn) 폴드아웃 펼침 여부
        private int  _selectedLink = -1;   // 선택 클립에서 '편집 중'인 링크 인덱스 (-1=없음) → 그 링크만 강조
        private int  _linkOwnerClip = -1;  // _selectedLink가 속한 클립 (클립 바뀌면 링크 선택 초기화용)

        // ── 드래그: Notify ────────────────────────────────────────
        private bool _draggingNotify;
        private int  _dragNotifyClip;
        private int  _dragNotifyIdx;

        // ── 드래그: Playhead ──────────────────────────────────────
        private bool _draggingPlayhead;

        // ── 드래그: Clip 순서 변경 ────────────────────────────────
        private int  _reorderingClip   = -1;
        private int  _reorderTargetIdx = -1;

        // ── 루트 모션 (PlayerController와 동일한 본 기반 방식) ─────
        private Transform _rootBone;            // 이동량 추출 본
        private Transform _bip001Bone;          // 메시 드리프트 방지용 XZ 리셋 본
        private float     _rootMotionScale = 1f;

        private int     _rmPrevClipIdx  = -1;   // 직전 샘플이 속한 클립
        private float   _rmPrevClipTime = 0f;   // 직전 샘플 클립 로컬 시간
        private Vector3 _rmPrevLocalPos;        // 직전 루트본 로컬 위치
        private bool    _rmHasPrev;             // 델타 계산용 prev 유효 여부
        private Vector3 _targetOriginPos;       // 재생 시작 시 target 위치 (리셋용)

        private Vector2 _inspScroll;

        // ── 라이브 모니터 (플레이 중 런타임 상태 추적) ────────────
        private PlayerStateMachine _liveMachine;
        private AnimationConfig    _liveConfig;
        private int                _liveClipIdx = -1;
        private string             _liveSection;
        private float              _liveNt;
        private bool               _liveHasBuffered;
        private ComboInput         _liveBuffered;
        private MoveDir            _liveMove = MoveDir.Any;
        [SerializeField] private bool _liveFollow = true;   // 런타임 config를 자동으로 따라가기
        [SerializeField] private AnimationConfig _preplayConfig;  // Play 진입 전 보던 config (종료 시 복원)

        // ── Notify 색상 ───────────────────────────────────────────
        private static readonly Color[] NotifyColors =
        {
            new Color(1.0f, 0.45f, 0.10f),
            new Color(0.2f, 0.75f, 1.00f),
            new Color(0.2f, 0.90f, 0.40f),
            new Color(0.9f, 0.30f, 0.90f),
        };

        [MenuItem("ZZZ/Animation Config Tool")]
        public static void Open()
        {
            var w = GetWindow<AnimationConfigTool>("Anim Config Tool");
            w.minSize = new Vector2(640f, 480f);
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // 도메인 리로드(Play 진입 등) 후 _config는 [SerializeField]로 살아남지만
            // SerializedObject는 직렬화 안 되므로 다시 만든다
            if (_config != null) _serializedConfig = new SerializedObject(_config);
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            ExitPreview();
        }

        // 플레이 진입/종료 시 에디터 프리뷰(AnimationMode)를 끔 — 런타임 애니메이터와 충돌 방지
        private void OnPlayModeChanged(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingEditMode)
            {
                ExitPreview();
                _preplayConfig = _config;   // Follow로 바뀌기 전 보던 config 기억
            }
            if (s == PlayModeStateChange.EnteredPlayMode)
                ExitPreview();
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                _liveMachine = null;
                _liveConfig  = null;
                // Play 중 Follow로 다른 config를 따라갔다면 원래 보던 것으로 복원
                if (_preplayConfig != null && _preplayConfig != _config)
                    ShowConfig(_preplayConfig);
            }
        }

        // ── Editor Update ────────────────────────────────────────
        private void OnEditorUpdate()
        {
            // 플레이 중에는 자체 프리뷰 대신 런타임 상태를 추적만 한다
            if (EditorApplication.isPlaying)
            {
                UpdateLiveState();
                return;
            }

            if (!_isPlaying || _config == null) return;
            double now   = EditorApplication.timeSinceStartup;
            float  delta = (float)(now - _lastEditorTime) * _previewSpeed;
            _lastEditorTime = now;

            if (_comboMode) ComboUpdate(delta);
            else            SequentialUpdate(delta);

            Repaint();
        }

        // 플레이 중인 씬에서 PlayerStateMachine을 찾아 현재 config/섹션/시간을 읽어온다
        private void UpdateLiveState()
        {
            if (_liveMachine == null)
                _liveMachine = UnityEngine.Object.FindFirstObjectByType<PlayerStateMachine>();
            if (_liveMachine == null) { _liveConfig = null; return; }

            _liveConfig      = _liveMachine.CurrentConfig;
            _liveClipIdx     = _liveMachine.CurrentClipIndex;
            _liveSection     = _liveMachine.CurrentSection;
            _liveNt          = _liveMachine.CurrentNormalizedTime;
            _liveHasBuffered = _liveMachine.HasBufferedInput;
            _liveBuffered    = _liveMachine.BufferedInput;
            _liveMove        = _liveMachine.CurrentMoveDir;

            // Follow ON이면 런타임 config를 자동으로 따라간다 (config가 비어 있으면 무조건 채움)
            if (_liveConfig != null && _liveConfig != _config && (_liveFollow || _config == null))
                ShowConfig(_liveConfig);

            Repaint();
        }

        // 창에 표시할 config를 교체 (SerializedObject 재생성 + 선택/스크롤 초기화)
        private void ShowConfig(AnimationConfig cfg)
        {
            _config = cfg;
            _serializedConfig = cfg != null ? new SerializedObject(cfg) : null;
            _selectedClip = -1; _selectedNotify = -1; _scrollX = 0f; _scrollY = 0f;
        }

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
                    _rmPrevClipIdx = -1;
                    _rmHasPrev     = false;
                    if (_target != null) _target.transform.position = _targetOriginPos;
                }
                else { _trackTime = total; _isPlaying = false; }
            }
            SampleAtTime(_trackTime, true);
        }

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
            _rmHasPrev       = false;
            _rmPrevClipIdx   = _comboActiveClip;
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
            _rmHasPrev       = false;
            _rmPrevClipIdx   = toIdx;
        }

        private string SectionLabel(int idx)
        {
            var c = _config.Clips[idx];
            return Short(!string.IsNullOrEmpty(c.SectionName) ? c.SectionName
                 : c.Clip != null ? c.Clip.name : $"Clip{idx}");
        }

        // 표시 전용: "_Ani_" 이전(캐릭터/리그 접두사)을 잘라 가독성 확보 (실제 데이터/값은 그대로).
        // 예: Avatar_Female_Size02_Burnice_Ani_Attack_Normal_01 → Attack_Normal_01.
        // 마커가 없으면 원본 그대로 ("(End/Entry)" 등).
        private const string k_nameMarker = "_Ani_";
        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int i = s.IndexOf(k_nameMarker, StringComparison.Ordinal);
            return i >= 0 ? s.Substring(i + k_nameMarker.Length) : s;
        }
        private static string[] ShortAll(string[] arr) => Array.ConvertAll(arr, Short);

        // ── Preview 제어 ─────────────────────────────────────────
        private void StartPreview()
        {
            if (_target == null || _config == null || _config.Clips.Count == 0) return;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            _isPlaying       = true;
            _lastEditorTime  = EditorApplication.timeSinceStartup;
            _rmPrevClipIdx   = -1;
            _rmPrevClipTime  = 0f;
            _rmHasPrev       = false;
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
        }

        private void SampleAtTime(float time, bool advancePlayback)
        {
            if (_target == null || _config == null) return;
            if (EditorApplication.isPlaying) return;   // 런타임 애니메이터와 충돌 방지
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();

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
            if (!tc.UseRootMotion || _rootBone == null) return;

            Vector3 curLocal = _rootBone.localPosition;

            if (advancePlayback)
            {
                bool sameClip = _rmPrevClipIdx == clipIdx;
                bool wrapped  = tc.IsLooping && clipTime < _rmPrevClipTime - 0.0001f; // 루프 되감김
                bool valid    = _rmHasPrev && sameClip && !wrapped;

                if (valid)
                {
                    Vector3 deltaLocal = curLocal - _rmPrevLocalPos;
                    _target.transform.position +=
                        _target.transform.TransformDirection(deltaLocal) * _rootMotionScale;
                }

                _rmPrevLocalPos = curLocal;
                _rmPrevClipTime = clipTime;
                _rmPrevClipIdx  = clipIdx;
                _rmHasPrev      = true;
            }

            ResetRootBoneVisual();
        }

        // 비주얼: 루트본 / Bip001 XZ 리셋 → 베이크된 이동량이 메시에 남는 것 방지
        private void ResetRootBoneVisual()
        {
            if (_rootBone == null) return;
            _rootBone.localPosition = Vector3.zero;
            if (_bip001Bone != null)
            {
                Vector3 lp = _bip001Bone.localPosition;
                lp.x = 0f; lp.z = 0f;
                _bip001Bone.localPosition = lp;
            }
        }

        // target에 PlayerController가 있으면 _rootBone/_bip001Bone/_rootMotionScale 자동 추출
        private void AutoDetectRootBones()
        {
            _rootBone = null; _bip001Bone = null; _rootMotionScale = 1f;
            if (_target == null) return;

            var pc = _target.GetComponentInChildren<ZZZ.Player.PlayerController>();
            if (pc == null) return;

            var so = new SerializedObject(pc);
            var rb = so.FindProperty("_rootBone");
            var bb = so.FindProperty("_bip001Bone");
            var sc = so.FindProperty("_rootMotionScale");
            if (rb != null) _rootBone        = rb.objectReferenceValue as Transform;
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

        // ── OnGUI ────────────────────────────────────────────────
        private void OnGUI()
        {
            DrawToolbar();
            if (_config != null && _serializedConfig != null) _serializedConfig.Update();

            var barRect = new Rect(0, ToolbarH, position.width, PlaybarH);
            if (EditorApplication.isPlaying) DrawLivebar(barRect);
            else                             DrawPlaybar(barRect);

            float contentY = ToolbarH + PlaybarH;
            float contentH = position.height - contentY;
            // 인스펙터 폭 = 창 너비 비율(클램프) → 큰 창일수록 넓게, 값 잘림 방지
            float inspW = Mathf.Clamp(position.width * 0.27f, 250f, 380f);
            float timelineW = Mathf.Max(100f, position.width - inspW - 1f);

            DrawTimeline(new Rect(0, contentY, timelineW, contentH));
            EditorGUI.DrawRect(new Rect(timelineW, contentY, 1f, contentH), new Color(0.1f, 0.1f, 0.1f));
            DrawInspector(new Rect(timelineW + 1f, contentY, inspW - 1f, contentH));

            if (_config != null && _serializedConfig != null)
                _serializedConfig.ApplyModifiedProperties();
        }

        // ── Toolbar ──────────────────────────────────────────────
        private void DrawToolbar()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, ToolbarH), new Color(0.2f, 0.2f, 0.2f));
            GUILayout.BeginArea(new Rect(4, 0, position.width - 8, ToolbarH));
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Target", GUILayout.Width(44));
            var pt = _target;
            _target = (GameObject)EditorGUILayout.ObjectField(_target, typeof(GameObject), true, GUILayout.Width(160));
            if (_target != pt)
            {
                ExitPreview(); _trackTime = 0f; AutoDetectRootBones();
                _poseBones = null;   // 본 캐시 무효화
                if (_target != null) _targetOriginPos = _target.transform.position;
            }

            GUILayout.Label("Config", GUILayout.Width(44));
            var pc = _config;
            _config = (AnimationConfig)EditorGUILayout.ObjectField(_config, typeof(AnimationConfig), false, GUILayout.Width(160));
            if (_config != pc)
            {
                ExitPreview(); _trackTime = 0f;
                _selectedClip = -1; _selectedNotify = -1;
                _serializedConfig = _config != null ? new SerializedObject(_config) : null;
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("New", GUILayout.Width(44)))
            {
                string path = EditorUtility.SaveFilePanelInProject("Create AnimationConfig",
                    "AnimationConfig", "asset", "저장 위치 선택");
                if (!string.IsNullOrEmpty(path))
                {
                    var a = CreateInstance<AnimationConfig>();
                    AssetDatabase.CreateAsset(a, path);
                    AssetDatabase.SaveAssets();
                    _config = a;
                    _serializedConfig = new SerializedObject(_config);
                    _trackTime = 0f;
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ── Livebar (플레이 중 런타임 상태 표시) ──────────────────
        private void DrawLivebar(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.14f, 0.19f, 0.14f));
            GUILayout.BeginArea(area);

            // 1행: LIVE 배지 + 현재 config/섹션/시간
            EditorGUILayout.BeginHorizontal();
            var liveStyle = new GUIStyle(EditorStyles.boldLabel)
            { normal = { textColor = new Color(0.4f, 1f, 0.45f) } };
            GUILayout.Label("● LIVE", liveStyle, GUILayout.Width(52));

            if (_liveMachine == null)
                GUILayout.Label("씬에서 PlayerStateMachine을 찾는 중…", EditorStyles.miniLabel);
            else if (_liveConfig == null)
                GUILayout.Label("현재 State가 ConfigState가 아님", EditorStyles.miniLabel);
            else
            {
                GUILayout.Label($"Config: {_liveConfig.name}", GUILayout.Width(180));
                var secStyle = new GUIStyle(EditorStyles.boldLabel)
                { normal = { textColor = new Color(1f, 0.85f, 0.3f) } };
                GUILayout.Label($"▶ {(_liveSection ?? "-")}", secStyle, GUILayout.Width(160));
                GUILayout.Label($"nt={Mathf.Repeat(_liveNt, 1f):F2}", GUILayout.Width(70));
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 2행: 입력 상태 + config 동기화 버튼
            EditorGUILayout.BeginHorizontal();
            if (_liveMachine != null)
            {
                GUILayout.Label($"Move: {_liveMove}", GUILayout.Width(120));
                var bufStyle = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = _liveHasBuffered ? InputColor(_liveBuffered) : Color.gray } };
                GUILayout.Label($"Buffered: {(_liveHasBuffered ? _liveBuffered.ToString() : "-")}",
                    bufStyle, GUILayout.Width(150));
            }

            // Follow: 런타임 config 자동 추적 (ON이면 전환 시 창도 따라감)
            var prevBg = GUI.backgroundColor;
            if (_liveFollow) GUI.backgroundColor = new Color(0.4f, 0.9f, 0.45f);
            _liveFollow = GUILayout.Toggle(_liveFollow, "Follow", "Button", GUILayout.Width(60));
            GUI.backgroundColor = prevBg;

            // Follow OFF이고 런타임과 다른 config를 보고 있으면 수동 전환 버튼
            if (!_liveFollow && _liveConfig != null && _liveConfig != _config)
            {
                if (GUILayout.Button("이 Config 표시", GUILayout.Width(110)))
                    ShowConfig(_liveConfig);
                GUILayout.Label("(다른 config 표시 중)", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        // ── Playbar ──────────────────────────────────────────────
        private void DrawPlaybar(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.22f, 0.22f, 0.22f));
            GUILayout.BeginArea(area);

            // ── 1행: 재생 컨트롤 ─────────────────────────────────
            EditorGUILayout.BeginHorizontal();

            string lbl = _isPlaying ? "■  Stop" : "▶  Play";
            if (GUILayout.Button(lbl, GUILayout.Height(24f), GUILayout.Width(72f)))
            {
                if (_isPlaying) StopPreview();
                else StartPreview();
            }
            if (GUILayout.Button("|◀", GUILayout.Height(24f), GUILayout.Width(28f)))
                ResetPreview();

            if (_comboMode)
                GUILayout.Label($"▶ {(_comboActiveClip >= 0 && _comboActiveClip < _config?.Clips.Count ? SectionLabel(_comboActiveClip) : "-")}",
                    GUILayout.Width(150));
            else
                GUILayout.Label($"{_trackTime:F2}s / {GetTotalDuration():F2}s", GUILayout.Width(90));

            GUILayout.Label("Speed", GUILayout.Width(38));
            _previewSpeed = EditorGUILayout.Slider(_previewSpeed, 0.1f, 2f, GUILayout.Width(90));
            GUILayout.Label("Scale", GUILayout.Width(36));
            _pxPerSec = EditorGUILayout.Slider(_pxPerSec, 20f, 250f, GUILayout.Width(90));

            EditorGUILayout.EndHorizontal();

            // ── 2행: 모드 + 콤보 입력 ────────────────────────────
            EditorGUILayout.BeginHorizontal();

            bool prevMode = _comboMode;
            _comboMode = GUILayout.Toggle(_comboMode, "Combo (Links)", "Button", GUILayout.Width(100f));
            if (_comboMode != prevMode) ResetPreview();

            if (_comboMode)
            {
                InputToggle(ComboInput.Normal,   "Normal",   64);
                InputToggle(ComboInput.Enhanced, "Enhanced", 72);
                InputToggle(ComboInput.Special,  "Special",  64);
                InputToggle(ComboInput.Dodge,    "Dodge",    56);
                if (GUILayout.Button("Clear", GUILayout.Width(48)))
                    System.Array.Clear(_heldInput, 0, _heldInput.Length);

                GUILayout.Label("Move", GUILayout.Width(36));
                _simMoveDir = (MoveDir)EditorGUILayout.EnumPopup(_simMoveDir, GUILayout.Width(72));

                DrawLoopToggle();
                GUILayout.Label(_comboLog, EditorStyles.miniLabel);
            }
            else
            {
                DrawLoopToggle();
                GUILayout.Label("순차 재생 — 트랙 순서대로", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawLoopToggle()
        {
            if (_config == null) return;
            var prevBg = GUI.backgroundColor;
            if (_config.LoopTrack) GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
            bool loop = GUILayout.Toggle(_config.LoopTrack, "↻ Loop", "Button", GUILayout.Width(60f));
            GUI.backgroundColor = prevBg;
            if (loop != _config.LoopTrack)
            {
                Undo.RecordObject(_config, "Toggle Loop Track");
                _config.LoopTrack = loop;
                EditorUtility.SetDirty(_config);
            }
        }

        // 눌러두면 유지되는 입력 토글
        private void InputToggle(ComboInput input, string label, float width)
        {
            bool held = _heldInput[(int)input];
            var prevBg = GUI.backgroundColor;
            if (held) GUI.backgroundColor = InputColor(input);
            bool next = GUILayout.Toggle(held, label, "Button", GUILayout.Width(width));
            GUI.backgroundColor = prevBg;
            _heldInput[(int)input] = next;
        }

        private void ResetPreview()
        {
            StopPreview();
            _trackTime       = 0f;
            _rmPrevClipIdx   = -1;
            _rmPrevClipTime  = 0f;
            _rmHasPrev       = false;
            _blending        = false;
            _comboLog        = "";
            if (_target != null && _config != null)
            {
                _target.transform.position = _targetOriginPos;
                if (_comboMode) { /* 포즈는 StartPreview에서 */ }
                else SampleAtTime(0f, false);
            }
        }

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
                        LinkTiming.WhenMatched  => link.WindowEnd,
                        LinkTiming.OnWindowMiss => link.WindowEnd,
                        LinkTiming.OnEnd        => 1f,
                        _                        => link.WindowEnd,
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

                // Section Turn 윈도우 (바 위 밴드)
                DrawSectionTurnWindow(tc, barX, barW, rowY);

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

        // Section Turn 윈도우 표시 — SectionTurn일 때 [TurnWindowStart, End] 구간을
        // 바 위에 보라 반투명 밴드 + 양끝 경계 마커로 그린다 (회전이 작동하는 normalizedTime 구간).
        private void DrawSectionTurnWindow(TrackClip tc, float barX, float barW, float rowY)
        {
            if (!tc.SectionTurn || barW <= 0f) return;

            float aN = Mathf.Clamp01(Mathf.Min(tc.TurnWindowStart, tc.TurnWindowEnd));
            float bN = Mathf.Clamp01(Mathf.Max(tc.TurnWindowStart, tc.TurnWindowEnd));
            float aX = barX + aN * barW;
            float bX = barX + bN * barW;
            float y  = rowY + 6f;
            float h  = ClipH - 12f;
            var   col = new Color(0.72f, 0.45f, 1f);   // 보라 = 회전

            EditorGUI.DrawRect(new Rect(aX, y, Mathf.Max(2f, bX - aX), h),
                new Color(col.r, col.g, col.b, 0.16f));
            EditorGUI.DrawRect(new Rect(aX - 1f, y, 2f, h), col);   // 시작 경계
            EditorGUI.DrawRect(new Rect(bX - 1f, y, 2f, h), col);   // 끝 경계

            if (bX - aX > 34f)
                GUI.Label(new Rect(aX + 3f, y + 1f, bX - aX - 4f, 11f), $"Turn {tc.TurnAngle:0}°",
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
                    case LinkTiming.WhenMatched:   aN = link.WindowStart; bN = link.WindowEnd; break;
                    case LinkTiming.OnWindowMiss:  aN = link.WindowEnd;   bN = 1f;             break;
                    case LinkTiming.OnEnd:         aN = 0.92f;            bN = 1f;             break;
                    default:                       aN = 0f;               bN = 1f;             break;
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

                    // Notify 클릭 우선
                    if (tc.Clip != null)
                    {
                        for (int ni = 0; ni < tc.Notifies.Count; ni++)
                        {
                            float mx = barX + tc.Notifies[ni].NormalizedTime * barW;
                            if (Mathf.Abs(ev.mousePosition.x - mx) <= 7f)
                            {
                                _selectedClip = i; _selectedNotify = ni; _notifyClipIdx = i;
                                _draggingNotify = true; _dragNotifyClip = i; _dragNotifyIdx = ni;
                                hitSomething = true;
                                break;
                            }
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
