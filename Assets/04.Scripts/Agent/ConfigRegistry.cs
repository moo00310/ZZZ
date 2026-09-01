using System.Collections.Generic;
using ZZZ;

namespace ZZZ.Agent
{
    // 등록된 config 모음 — 시작/기본 config + 이벤트 진입용 추가 config들.
    // 재생할 섹션 이름으로 해당 config를 자동 검색한다 (Hit/Dodge 등이 어떤 config를 쓸지 결정).
    public class ConfigRegistry
    {
        public AnimationConfig StartConfig { get; }
        private readonly List<AnimationConfig> _configs;

        public ConfigRegistry(AnimationConfig startConfig, List<AnimationConfig> configs)
        {
            StartConfig = startConfig;
            _configs    = configs;
        }

        // 해당 섹션을 가진 첫 config 반환 (시작 config 우선, 없으면 null)
        public AnimationConfig FindWithSection(string section)
        {
            if (StartConfig != null && StartConfig.IndexOfSection(section) >= 0) return StartConfig;
            if (_configs != null)
                foreach (var c in _configs)
                    if (c != null && c.IndexOfSection(section) >= 0) return c;
            return null;
        }
    }
}
