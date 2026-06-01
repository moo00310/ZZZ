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
        public bool  Loop          = false;
        public bool  UseRootMotion = false;
        public float Speed         = 1f;
        public float TransitionIn  = 0.1f;  // 이전 → 이 클립 CrossFade 시간

        [Header("Links")]   // 이 섹션에서 분기 가능한 다음 섹션들
        public List<ClipLink> Links = new List<ClipLink>();

        public List<TrackNotify> Notifies = new List<TrackNotify>();
    }

    // Unreal Montage의 Section Link에 해당.
    // "이 입력이 이 윈도우 구간 안에 들어오면 → TargetSection으로 전이"
    [Serializable]
    public class ClipLink
    {
        public string     TargetSection = "";   // 빈 값 = 트랙 종료(복귀)
        public ComboInput  Input        = ComboInput.Normal;

        [Range(0f, 1f)] public float WindowStart = 0.5f;  // 입력 수용 시작 (normalizedTime)
        [Range(0f, 1f)] public float WindowEnd   = 1.0f;  // 입력 수용 끝
    }

    public enum ComboInput
    {
        Normal,
        Enhanced,
        Special,
        Dodge,
        Any
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
