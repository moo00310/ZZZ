using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ;
using ZZZ.Player.StateMachine;

namespace ZZZ.Editor.AnimationTool
{
    public partial class AnimationConfigTool : EditorWindow
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
        private bool[]  _heldInput = new bool[7]; // 토글로 눌러둔 입력 (ComboInput 인덱스 — None/Parry 포함 크기)
        private MoveDir _simMoveDir = MoveDir.Neutral;  // 시뮬레이션 이동 방향
        private string  _comboLog = "";   // 전이 흐름 로그
        // OnEndIfMatched 링크의 윈도우 래치 (프리뷰용) — 섹션 전이마다 비운다
        private readonly System.Collections.Generic.HashSet<ClipLink> _previewLatched
            = new System.Collections.Generic.HashSet<ClipLink>();

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
        private Transform _bip001Bone;          // 이동량 추출 본 — 수평(X·Z) 델타 추출, 메시는 X·Z 0 리셋 / Y 유지
        private float     _rootMotionScale = 1f;

        private RootMotionTracker _rmTracker;   // 루트본 로컬 델타 누적기 (순수 계산 — 단위 테스트 대상)
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
    }
}
