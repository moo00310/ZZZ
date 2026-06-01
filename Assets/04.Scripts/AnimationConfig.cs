using System;
using System.Collections.Generic;
using UnityEngine;

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
        public float DoneThreshold  = 0.85f;  // normalizedTime >= 이 값이면 "클립 끝" 판정

        [Header("Entry")]
        public string EntrySection = "";      // 트랙 시작 시 재생할 섹션 (빈 값 = 첫 클립)

        public List<TrackClip> Clips = new List<TrackClip>();

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

    // 섹션 간 전이 정의. Trigger 종류에 따라 발동 조건이 다름.
    [Serializable]
    public class ClipLink
    {
        // 전이 대상. TargetConfig가 비어있으면 현재 config 내 전이,
        // 지정되면 그 config로 갈아끼우고 TargetSection(비면 EntrySection)으로 진입.
        public AnimationConfig TargetConfig;        // null = 현재 config
        public string          TargetSection = "";  // 빈 값 = (현재)복귀 / (타겟)EntrySection
        public LinkTrigger     Trigger       = LinkTrigger.Input;
        public float           BlendDuration = 0.1f;  // 이 전이가 발동할 때 CrossFade 시간(초)

        [Header("Input Trigger")]
        public ComboInput Input = ComboInput.Normal;  // Trigger=Input일 때 공격 종류
        [Range(0f, 1f)] public float WindowStart = 0.5f;  // 입력 수용 시작
        [Range(0f, 1f)] public float WindowEnd   = 1.0f;  // 입력 수용 끝

        [Header("Condition (모든 트리거에 AND)")]
        public MoveDir Move = MoveDir.Any;   // 이동 조건
    }

    // 전이를 일으키는 트리거 종류
    public enum LinkTrigger
    {
        Input,      // 입력이 윈도우 구간 안에 들어옴 (콤보)
        OnEnd,      // 클립이 끝나면 다른 섹션으로 전이 (WalkEnd→Idle 등). 루프 클립엔 무효
        WhileMove   // Move 조건이 충족되는 동안 즉시 (Idle→Walk 등)
    }

    public enum ComboInput
    {
        Normal,
        Enhanced,
        Special,
        Dodge,
        Any
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
