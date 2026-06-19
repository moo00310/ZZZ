using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace ZZZ
{
    [CreateAssetMenu(menuName = "ZZZ/Animation Config", fileName = "AnimationConfig")]
    public class AnimationConfig : ScriptableObject
    {
        public string TrackName = "New Track";

        [Header("Playback")]
        public bool LoopTrack = false;        // 트랙 끝까지 재생 후 처음으로 되돌려 무한 반복

        [Header("Combo Timing")]
        public float DoneThreshold  = 0f;     // OnEnd 발동 normalizedTime. 0 = 자동(클립 마지막 프레임 = 1 - 1/frames)

        [Header("Entry")]
        public string EntrySection = "";      // 트랙 시작 시 재생할 섹션 (빈 값 = 첫 클립)

        public List<TrackClip> Clips = new List<TrackClip>();

        [Header("Global Links — 모든 클립에 적용 (Any State 전이)")]
        // 여기에 둔 링크는 어떤 섹션이 재생 중이든 매 프레임 평가된다.
        // 예: Idle config 어디서든 이동 입력 시 Walk로. 클립마다 같은 링크를 달 필요 없음.
        public List<ClipLink> GlobalLinks = new List<ClipLink>();

        // 섹션 이름으로 클립 인덱스 검색 (-1 = 없음)
        public int IndexOfSection(string section)
        {
            if (string.IsNullOrEmpty(section)) return -1;
            for (int i = 0; i < Clips.Count; i++)
                if (Clips[i].SectionName == section) return i;
            return -1;
        }
    }

    [Serializable]
    public class TrackClip
    {
        public string        SectionName = "";   // 섹션 식별자 (Montage 스타일)
        public AnimationClip Clip;

        [Header("Playback")]
        public float Speed = 1f;

        [Header("Movement")]
        public MoveMode MoveMode     = MoveMode.None;  // 이 클립 동안 캐릭터 이동 방식
        public bool     LockRotation = false;          // true면 이 클립 동안 이동 입력이 있어도 캐릭터 회전 금지 (피격/경직)
        // LockRotation 작동 구간 (normalizedTime). End<=Start면 섹션 전체 잠금(기존 동작).
        [Range(0f, 1f)] public float LockWindowStart = 0f;
        [Range(0f, 1f)] public float LockWindowEnd   = 1f;
        // (루프 RootMotion 전용) 클립 끝 정지프레임이 만드는 전진 '틱'을 한 루프 평균 전진속도로 제거.
        // 기본 끔 — 루프 섹션(걷기/달리기)에서만 켤 것. 비루프/비RootMotion 클립은 무시됨.
        public bool     SmoothLoopSpeed = false;
        // 진입 순간 현재 이동 입력 방향으로 즉시 스냅 (공격 첫 프레임 조준). LockRotation과 함께 쓰면
        // "진입 때 한 번 조준 → 휘두름 중 고정"이 된다. 입력이 있으면 SnapRotation(적 방향)보다 우선.
        public bool     FaceInputOnEnter = false;

        [Header("Start Boost — 시작 시 진행방향 초기 이동(루트모션 워밍업 보완)")]
        public float StartBoostSpeed = 0f;   // 클립 시작 순간 속도(0 = 끔). 시간이 지나며 0으로 감쇠
        public float StartBoostTime  = 0.15f; // 부스트가 0으로 감쇠하기까지의 시간(초)

        [Header("Target Tracking — 전방 적에게 루트모션 워프 (RootMotion 전용)")]
        // 섹션 진입 시 전방 적을 찾아 루트모션 수평 이동을 적 방향으로 재조준한다.
        // 적이 없으면 보정량 0 → 원본 루트모션 그대로 (적 유무 분기 불필요)
        public bool  EnableTracking = false;
        [Range(0f, 1f)] public float TrackWindowStart = 0f;    // 보정 작동 구간 (normalizedTime)
        [Range(0f, 1f)] public float TrackWindowEnd   = 0.4f;  // 타격 이후엔 끊어야 적을 따라 휙 돌지 않음
        public float StopDistance = 1.2f;   // 타겟 앞 정지 거리 (관통 방지)
        public bool  SnapRotation = true;   // 섹션 진입 시 타겟 방향으로 즉시 회전

        [Header("Section Turn — 구워진 턴 회전을 transform으로 추출 (RootMotion 전용)")]
        // 클립에 구워진 회전(턴)을 Bip001 yaw 델타로 매 프레임 뽑아 transform에 적용한다.
        // 메시 본 yaw는 섹션 시작 기준으로 카운터-회전해 이중 회전(360처럼 보임)을 막는다 →
        // 회전 출처가 transform 하나로 통일되어, 턴 이후 전진 루트모션이 새 facing으로 나간다.
        // 켤 때 LockRotation=true도 같이 둘 것 (입력 기반 회전과 충돌 방지).
        public bool  SectionTurn = false;
        // yaw 델타를 추출하는 구간 (normalizedTime). End<=Start면 섹션 전체.
        // 구간 밖에서도 이미 추출한 회전의 카운터/이동보정은 유지(팝 방지) — 추출만 멈춘다.
        [Range(0f, 1f)] public float TurnWindowStart = 0f;
        [Range(0f, 1f)] public float TurnWindowEnd   = 1f;

        [Header("Links")]   // 이 섹션에서 분기 가능한 다음 섹션들
        public List<ClipLink> Links = new List<ClipLink>();

        public List<TrackNotify> Notifies = new List<TrackNotify>();

        [Header("Modules — 섹션 기능 (i-frame 등). 있는 것만 실행 (폴리모픽)")]
        [SerializeReference] public List<SectionModule> Modules = new List<SectionModule>();

        public bool UseRootMotion => MoveMode == MoveMode.RootMotion;

        // 루프 여부는 클립 임포트 설정(Loop Time)에서 가져온다 — config가 따로 관리하지 않음(표시용)
        public bool IsLooping => Clip != null && Clip.isLooping;
    }

    // 클립 재생 중 캐릭터 이동 방식.
    // None = 루트모션 안 씀(중력만 코드 적용). RootMotion = Bip001 본 이동량을 적용.
    // 값 1은 과거 Planar(코드 이동) 자리 — 폐기했지만 RootMotion=2 직렬화 호환 위해 비워 둔다.
    public enum MoveMode
    {
        None       = 0,   // 제자리 (이동 없음, 중력만)
        RootMotion = 2    // 본(Bip001) 이동량 적용 — 걷기/달리기/공격/대시
    }

    // 섹션 간 전이 정의.
    // 조건(Attack + Direction)이 타이밍(Timing) 규칙에 맞게 충족되면 전이한다.
    //   조건 = 공격 입력 + 방향 입력 (둘 다 AND)
    //   타이밍 = 언제 그 조건을 평가/발동할지 (즉시 / 윈도우 통과 시 / 클립 끝)
    [Serializable]
    public class ClipLink
    {
        // 전이 대상. TargetConfig가 비어있으면 현재 config 내 전이,
        // 지정되면 그 config로 갈아끼우고 TargetSection(비면 EntrySection)으로 진입.
        public AnimationConfig TargetConfig;        // null = 현재 config
        public string          TargetSection = "";  // 빈 값 = (현재)복귀 / (타겟)EntrySection
        public float           BlendDuration = 0.01f;  // 이 전이가 발동할 때 CrossFade 시간(초)

        [Header("Condition — 공격/방향 입력 조건 (AND)")]
        [FormerlySerializedAs("Input")]
        public ComboInput Attack    = ComboInput.None;  // 요구 공격 입력 (None = 공격 없음)
        [FormerlySerializedAs("Move")]
        public MoveDir    Direction = MoveDir.Any;       // 요구 방향 입력 (Any = 상관없음)

        [Header("Timing — 언제 평가할지")]
        public LinkTiming Timing = LinkTiming.WhenMatched;
        [Range(0f, 1f)] public float WindowStart = 0f;   // 평가 구간 시작 (normalizedTime)
        [Range(0f, 1f)] public float WindowEnd   = 1f;   // 평가 구간 끝
    }

    // 전이 조건을 언제 평가/발동할지
    public enum LinkTiming
    {
        WhenMatched,   // 윈도우 구간 안에서 조건이 충족되는 즉시 (콤보 입력 / 방향 이동 / 복귀)
        OnWindowMiss,  // 윈도우 끝까지 조건이 충족되지 않고 지나가면 (콤보 캔슬 / 타임아웃)
        OnEnd          // 클립이 끝나면 (조건은 가드로 작동). 루프 클립엔 무효
    }

    public enum ComboInput
    {
        Normal,
        Enhanced,
        Special,
        Dodge,
        Any,        // 아무 공격 입력
        None        // 공격 입력 없음 (※ 직렬화 호환 위해 맨 끝에 추가)
    }

    // 이동키 방향 조건
    public enum MoveDir
    {
        Any,        // 이동 상관없음
        Neutral,    // 이동 입력 없음 (정지)
        Moving,     // 아무 방향이든 이동 중
        Forward,    // W
        Back,       // S
        Left,       // A
        Right,      // D
        // 관계형 — 카메라 절대 방향이 아니라 "현재 진행(facing) 방향의 반대"로 입력.
        // 절대 enum끼리 비교(MoveMatches)로는 못 잡아 ConfigState에서 dot으로 특수 처리한다.
        // 런 루프 중 반대키 입력 → 180 턴 전이용.
        Reverse
    }

    [Serializable]
    public class TrackNotify
    {
        public NotifyType Type;
        [Range(0f, 1f)]
        public float      NormalizedTime;
        public string     EventName    = "";
        public GameObject EffectPrefab;
    }

    public enum NotifyType
    {
        Effect,
        Camera,
        Sound,
        Custom
    }
}
