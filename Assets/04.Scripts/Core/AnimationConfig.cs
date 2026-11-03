using System;
using System.Collections.Generic;
using UnityEngine;
using ZZZ.Effects;

namespace ZZZ
{
    // 캐릭터 동작 하나(회피, 일반 공격 콤보 등)를 이루는 섹션과 전이 규칙.
    // CharacterActionRunner가 이 데이터를 해석하고, Animator는 클립 재생만 맡는다.
    [CreateAssetMenu(menuName = "ZZZ/Animation Config", fileName = "AnimationConfig")]
    public class AnimationConfig : ScriptableObject
    {
        public string TrackName = "New Track";

        // 편집 툴의 콤보 미리보기 전용 설정이다. 런타임 재생에는 영향이 없다.
        [Header("Editor Preview")]
        public bool LoopTrack = false;

        // OnEnd·OnEndIfMatched 링크가 '클립 끝'으로 보는 진행도. 0이면 클립의 마지막 프레임 시점을 쓴다.
        [Header("Combo Timing")]
        public float DoneThreshold = 0f;

        // 이 config로 진입할 때 재생할 섹션. 비어 있으면 첫 섹션부터 재생한다.
        [Header("Entry")]
        public string EntrySection = "";

        public List<TrackClip> Clips = new List<TrackClip>();

        // 재생 중인 섹션과 관계없이 매 프레임 평가하는 링크(Animator의 Any State 전이에 해당).
        // 모든 섹션에 같은 링크를 반복해 달지 않으려고 둔다. 예: 대기 중 어디서든 이동 입력이 오면 걷기로.
        [Header("Global Links")]
        public List<ClipLink> GlobalLinks = new List<ClipLink>();

        public int IndexOfSection(string section)
        {
            if (string.IsNullOrEmpty(section)) return -1;
            for (int i = 0; i < Clips.Count; i++)
                if (Clips[i].SectionName == section) return i;
            return -1;
        }
    }

    // 섹션 하나는 클립 하나와, 그 클립에 붙은 링크·Notify·모듈로 이루어진다.
    [Serializable]
    public class TrackClip
    {
        public string        SectionName = "";
        public AnimationClip Clip;

        [Header("Playback")]
        public float Speed = 1f;

        [Header("Movement")]
        public MoveMode MoveMode = MoveMode.None;

        // GlobalLinks보다 먼저, 위에서부터 평가해 처음 발동한 링크 하나만 탄다. 순서가 곧 우선순위다.
        [Header("Links")]
        public List<ClipLink> Links = new List<ClipLink>();

        public List<TrackNotify> Notifies = new List<TrackNotify>();

        // 무적·패링·타깃 워프 같은 구간 기능. 실행기는 구체 타입을 모른 채 호출한다.
        [Header("Modules")]
        [SerializeReference] public List<SectionModule> Modules = new List<SectionModule>();

        public bool UseRootMotion => MoveMode == MoveMode.RootMotion;

        // 루프 여부는 config에 따로 두지 않고 클립 임포트 설정(Loop Time)을 따른다.
        public bool IsLooping => Clip != null && Clip.isLooping;
    }

    // 섹션 재생 중 캐릭터 이동 방식.
    // 값 1은 비워 둔다. 기존 에셋에 RootMotion이 정수 2로 저장돼 있어서, 번호를 당기면 값이 달라진다.
    public enum MoveMode
    {
        None       = 0,   // Animator 루트 델타를 버리고 제자리에 둔다 (중력만 적용)
        RootMotion = 2    // Animator 루트 모션을 그대로 적용한다
    }

    // 섹션 간 전이. '언제 볼지'(Timing, Window)와 '무엇을 볼지'(Condition)를 나눠 둔다.
    // 덕분에 플레이어 입력 조건과 몬스터 AI 조건이 같은 타이밍 규칙을 그대로 쓴다.
    [Serializable]
    public class ClipLink
    {
        // TargetConfig가 비어 있으면 현재 config 안의 TargetSection으로 간다. 그 섹션이 없으면 캐릭터의 기본 config로 돌아간다.
        // TargetConfig를 지정하면 그 config로 바꾸고 TargetSection(비어 있으면 그 config의 EntrySection)으로 진입한다.
        public AnimationConfig TargetConfig;
        public string          TargetSection = "";
        public float           BlendDuration = 0.01f;   // CrossFade 시간(초)

        // 대상 섹션을 이 진행도부터 재생한다(윈드업 생략 등). 이 지점 이전의 Notify는 발동하지 않는다.
        // 링크 전이에만 적용되고, 회피·피격 트리거가 InterruptWith로 끼어드는 진입에는 적용되지 않는다.
        [Range(0f, 1f)] public float EntryOffset = 0f;

        // 비어 있으면 항상 충족으로 취급한다.
        [Header("Condition")]
        [SerializeReference] public LinkCondition Condition;

        // Window는 조건을 보는 구간이다. OnEnd는 Window를 쓰지 않는다.
        [Header("Timing")]
        public LinkTiming Timing = LinkTiming.WhenMatched;
        [Range(0f, 1f)] public float WindowStart = 0f;
        [Range(0f, 1f)] public float WindowEnd   = 1f;
    }

    public enum LinkTiming
    {
        // Window 안에서 조건이 충족되는 즉시 전이한다. 콤보 입력, 이동 캔슬 등.
        WhenMatched,

        // Window 안에서 조건의 '뗌' 신호가 오면 전이한다. 홀드 차지 후 발사용.
        // 누르고 있는 동안에는 전이하면 안 되므로, 누름을 보는 Matches 대신 ReleaseTriggered로 판정한다.
        OnRelease,

        // 클립 끝에서 조건이 충족돼 있으면 전이한다. 루프 클립은 매 반복의 끝에서 판정한다.
        OnEnd,

        // Window 안에서 조건이 한 번이라도 충족되면 기억해 두었다가 클립 끝에서 전이한다.
        // WhenMatched가 즉시 캔슬이라면, 이쪽은 끝까지 재생한 뒤 입력 여부로 분기한다(카운터 예약 등).
        // 충족된 적이 없으면 발동하지 않으므로, 기본 전이는 뒤에 OnEnd 링크로 따로 둔다.
        OnEndIfMatched
    }

    // Unity는 enum을 이름이 아니라 정수로 직렬화한다(.asset의 "Attack: 2").
    // 순서를 바꾸면 기존 에셋의 값이 다른 입력을 가리키게 되므로, 새 값은 끝에 추가한다.
    public enum ComboInput
    {
        None,     // 공격 입력을 요구하지 않음
        Any,      // 어떤 공격 입력이든
        Normal,   // 일반 공격 (좌클릭 탭)
        Strong,   // 강공격 (좌클릭 홀드)
        Enhance,  // 강화 공격 (E)
        Dodge,    // 회피
        Parry     // 패링
    }

    // 적 공격의 강도. 피격 반응과 패링 성공 시 반응(ParryAid_L/H)을 고르는 데 쓴다.
    // 적이 공격을 예고할 때(OpenIncomingAttack) 함께 넘긴다.
    public enum AttackStrength
    {
        Light,
        Heavy
    }

    // 링크 조건에 쓰는 이동 입력 방향.
    public enum MoveDir
    {
        Any,        // 이동 입력과 관계없음
        Neutral,    // 이동 입력 없음
        Moving,     // 방향과 관계없이 이동 중
        Forward,
        Back,
        Left,
        Right,
        // 고정 방향이 아니라 '현재 바라보는 방향의 반대' 입력. 달리기 중 반대키로 180도 턴할 때 쓴다.
        // 고정 방향끼리 비교해서는 판정할 수 없어 InputCondition에서 내적으로 따로 판정한다.
        Reverse
    }

    [Serializable]
    public class TrackNotify
    {
        [Range(0f, 1f)]
        public float NormalizedTime;

        [SerializeReference] private NotifyPayload _payload;

        // NormalizedTime보다 크면 구간 Notify가 된다. 이펙트는 이 구간 동안 방출을 유지하고(트레일, 오라, 차지 등),
        // 타격은 이 구간 동안 판정을 유지한다. NormalizedTime 이하이면 그 시점에 한 번만 발동한다.
        [Range(0f, 1f)]
        public float EndNormalizedTime = 0f;

        // 편집 툴에서 드래그로 시점이 밀리지 않게 고정한다. 값이 확정된 Notify를 실수로 옮기지 않기 위해서이며,
        // 선택·편집·삭제는 그대로 할 수 있다.
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
