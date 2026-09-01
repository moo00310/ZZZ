using System;
using System.Collections.Generic;
using UnityEngine;
using ZZZ.Effects;

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
    // None = Animator 루트 델타를 버림(중력만 적용). RootMotion = Animator.deltaPosition/deltaRotation을 적용.
    // 값 1은 과거 Planar(코드 이동) 자리 — 폐기했지만 RootMotion=2 직렬화 호환 위해 비워 둔다.
    public enum MoveMode
    {
        None       = 0,   // 제자리 (이동 없음, 중력만)
        RootMotion = 2    // Animator 루트 모션 적용 — 걷기/달리기/공격/대시
    }

    // 섹션 간 전이 정의.
    // 조건(Condition, 폴리모픽)이 타이밍(Timing) 규칙에 맞게 충족되면 전이한다.
    //   조건 = LinkCondition (InputCondition=공격+방향 / AlwaysCondition / 몬스터 거리·체력 등)
    //   타이밍 = 언제 그 조건을 평가/발동할지 (즉시 / 윈도우 통과 시 / 클립 끝 / 릴리스)
    [Serializable]
    public class ClipLink
    {
        // 전이 대상. TargetConfig가 비어있으면 현재 config 내 전이,
        // 지정되면 그 config로 갈아끼우고 TargetSection(비면 EntrySection)으로 진입.
        public AnimationConfig TargetConfig;        // null = 현재 config
        public string          TargetSection = "";  // 빈 값 = (현재)복귀 / (타겟)EntrySection
        public float           BlendDuration = 0.01f;  // 이 전이가 발동할 때 CrossFade 시간(초)
        // 이 전이로 진입 시 대상 섹션을 이 normalizedTime 지점부터 재생 (0 = 처음부터). 윈드업 스킵 등.
        // 이 지점 이전의 Notify는 발동하지 않는다. ※ E 트리거(InterruptWith) 진입엔 적용 안 됨 — 링크 전이 전용.
        [Range(0f, 1f)] public float EntryOffset = 0f;

        [Header("Condition — 전이 조건 (다형성)")]
        // 무엇이 충족인지를 정의 — 플레이어 입력(InputCondition) / 무조건(AlwaysCondition) / (몬스터)거리·체력 등.
        // null이면 CharacterActionRunner가 항상 true(Always)로 취급한다. [SerializeReference] 다형성 .
        [SerializeReference] public LinkCondition Condition;

        [Header("Timing — 언제 평가할지")]
        public LinkTiming Timing = LinkTiming.WhenMatched;
        [Range(0f, 1f)] public float WindowStart = 0f;   // 평가 구간 시작 (normalizedTime)
        [Range(0f, 1f)] public float WindowEnd   = 1f;   // 평가 구간 끝
    }

    // 전이 조건을 언제 평가/발동할지
    public enum LinkTiming
    {
        WhenMatched,    // 윈도우 구간 안에서 조건이 충족되는 즉시 (콤보 입력 / 방향 이동 / 복귀)
        OnRelease,      // 이 링크의 Attack 키를 손에서 뗀 순간 발동 (홀드 차지 → 릴리스).
                        //   [WindowStart,End] 안에서 떼야 함. press 버퍼가 아니라 릴리스 신호를 보므로
                        //   CharacterActionRunner에서 ConditionMatches 게이트를 우회해 따로 처리한다.
        OnEnd,          // 클립이 끝나면 (조건은 가드로 작동). 루프 클립엔 무효
        OnEndIfMatched  // 윈도우 안에서 조건이 '한 번이라도' 충족되면 래치 → 섹션 끝에 발동.
                        //   WhenMatched=즉시 캔슬, 이건 "섹션 끝까지 재생 후 입력 여부로 분기"(카운터 예약 등).
                        //   래치 안 되면 발동 안 함 → 뒤에 둔 OnEnd(Attack=None) 등으로 폴백.
    }

    // ※ Unity는 이 enum을 '정수'로 직렬화한다(.asset의 Attack: N은 이름이 아니라 인덱스).
    //    순서를 바꾸면 정수가 바뀌므로 기존 config의 Attack 값을 함께 리맵해야 한다.
    public enum ComboInput
    {
        None,     // 공격 입력 없음 (특수 토큰)
        Any,      // 아무 공격 (특수 토큰)
        Normal,   // 일반공격 — 좌클릭 탭
        Strong,   // 강공격 — 좌클릭 홀드
        Enhance,  // 강화공격 — E
        Dodge,    // 회피 (push 트리거)
        Parry     // 패링 (push 트리거)
    }

    // 적 공격의 강도 — 일반 피격 반응과 패링 쳐냄(ParryAid_L/H)을 결정한다.
    // 적 공격 시스템이 OpenIncomingAttack에 실어 보낸다 (예고 시점에 선언).
    public enum AttackStrength
    {
        Light,   // 약공 → ParryAid_L
        Heavy    // 강공 → ParryAid_H
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
        // 절대 enum끼리 비교(MoveMatches)로는 못 잡아 CharacterActionRunner에서 dot으로 특수 처리한다.
        // 런 루프 중 반대키 입력 → 180 턴 전이용.
        Reverse
    }

    [Serializable]
    public class TrackNotify
    {
        [Range(0f, 1f)]
        public float      NormalizedTime;

        [SerializeReference] private NotifyPayload _payload;

        // 구간(Interval) 이펙트 — End > NormalizedTime이면 [NormalizedTime, End] 동안 '유지'되는 지속 연출.
        // 시작 시점에 스폰해 계속 방출하다가 End에서(또는 섹션 이탈/캔슬 시) 방출을 멈춘다 —
        // 트레일/오라/차지처럼 '한 시점'이 아니라 '구간'에 걸리는 이펙트용. End<=Time = 기존 단발(point).
        // Effect 타입에만 의미. 프리팹은 루프 방출 + DespawnMode.ParticleStopped 권장(정지 후 자연 소멸).
        [Range(0f, 1f)]
        public float EndNormalizedTime = 0f;

        // 고정(Lock): 켜지면 타임라인에서 드래그로 시점이 밀리지 않는다 — 값이 확정된 Notify를
        // 실수로 옮기는 사고 방지용. 선택/편집(인스펙터)·삭제는 그대로 가능, 이동만 막는다.
        public bool Locked = false;

        public NotifyPayload Payload
        {
            get
            {
                EnsurePayload();
                return _payload;
            }
        }

        public NotifyType Type
        {
            get
            {
                EnsurePayload();
                return _payload.Type;
            }
            set => ChangePayloadType(value);
        }

        public ConfigEventType ConfigEvent
        {
            get => Payload is CustomNotifyPayload payload
                ? payload.EventType
                : ConfigEventType.None;
            set
            {
                if (Payload is CustomNotifyPayload payload)
                    payload.EventType = value;
            }
        }

        public CompositeEffect Effect
        {
            get => Payload is EffectNotifyPayload payload ? payload.Effect : null;
            set
            {
                if (Payload is EffectNotifyPayload payload) payload.Effect = value;
            }
        }

        public HitData Hit
        {
            get => Payload switch
            {
                HitNotifyPayload hitPayload => hitPayload.Hit,
                EffectNotifyPayload effectPayload => effectPayload.Hit,
                _ => null,
            };
            set
            {
                switch (Payload)
                {
                    case HitNotifyPayload hitPayload:
                        hitPayload.Hit = value;
                        break;
                    case EffectNotifyPayload effectPayload:
                        effectPayload.Hit = value;
                        break;
                }
            }
        }

        public EffectTransitionMode TransitionMode
        {
            get => Payload is EffectNotifyPayload payload
                ? payload.TransitionMode
                : EffectTransitionMode.Keep;
            set
            {
                if (Payload is EffectNotifyPayload payload) payload.TransitionMode = value;
            }
        }

        public string NextSection
        {
            get => Payload is EffectNotifyPayload payload ? payload.NextSection : "";
            set
            {
                if (Payload is EffectNotifyPayload payload) payload.NextSection = value;
            }
        }

        public bool IsInterval => EndNormalizedTime > NormalizedTime;

        public bool EnsurePayload()
        {
            if (_payload != null) return false;

            _payload = new EffectNotifyPayload();
            return true;
        }

        private void ChangePayloadType(NotifyType type)
        {
            EnsurePayload();
            if (_payload.Type == type) return;

            ConfigEventType configEvent = ConfigEvent;
            CompositeEffect effect = Effect;
            HitData hit = Hit;
            EffectTransitionMode transitionMode = TransitionMode;
            string nextSection = NextSection;
            _payload = CreatePayload(
                type, configEvent, effect, hit, transitionMode, nextSection);
        }

        private static NotifyPayload CreatePayload(
            NotifyType type, ConfigEventType configEvent,
            CompositeEffect effect, HitData hit,
            EffectTransitionMode transitionMode, string nextSection)
        {
            switch (type)
            {
                case NotifyType.Effect:
                    return new EffectNotifyPayload(
                        effect, null, transitionMode, nextSection);
                case NotifyType.Camera:
                    return new CameraNotifyPayload();
                case NotifyType.Sound:
                    return new SoundNotifyPayload();
                case NotifyType.Custom:
                    return new CustomNotifyPayload(configEvent);
                case NotifyType.Hit:
                    return new HitNotifyPayload(hit);
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }

    public enum EffectTransitionMode
    {
        Keep,
        Stop,
        Next
    }

    public enum NotifyType
    {
        Effect,
        Camera,
        Sound,
        Custom,
        Hit
    }
}
