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
        public float ComboResetTime = 1.2f;   // 입력 없으면 이 시간 후 복귀
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
        public bool  Loop  = false;  // 이 클립을 반복 재생할지 (Idle/WalkLoop 등)
        public float Speed = 1f;

        [Header("Movement")]
        public MoveMode MoveMode  = MoveMode.None;  // 이 클립 동안 캐릭터 이동 방식
        public float    MoveSpeed = 4f;             // MoveMode.Planar일 때 코드 이동 속도

        [Header("Links")]   // 이 섹션에서 분기 가능한 다음 섹션들
        public List<ClipLink> Links = new List<ClipLink>();

        public List<TrackNotify> Notifies = new List<TrackNotify>();

        public bool UseRootMotion => MoveMode == MoveMode.RootMotion;
    }

    // 클립 재생 중 캐릭터 이동 방식
    public enum MoveMode
    {
        None,        // 제자리 (이동 없음)
        Planar,      // 입력 방향으로 코드 이동 (MoveSpeed) — 걷기/달리기 루프
        RootMotion   // 루트본 이동량 적용 — 공격/대시
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
        public float           BlendDuration = 0.1f;  // 이 전이가 발동할 때 CrossFade 시간(초)

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
        Right       // D
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
