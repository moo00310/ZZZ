using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Monster
{
    [DisallowMultipleComponent]
    public sealed class MonsterRegistry : MonoBehaviour
    {
        private readonly HashSet<MonsterAI> _activeMonsters =
            new HashSet<MonsterAI>();

        private bool _aiEnabled = true;

        public IReadOnlyCollection<MonsterAI> ActiveMonsters =>
            _activeMonsters;
        public bool AIEnabled => _aiEnabled;

        public void Register(MonsterAI monster)
        {
            if (monster == null) return;

            _activeMonsters.Add(monster);
            monster.SetDecisionEnabled(_aiEnabled);
        }

        public void Unregister(MonsterAI monster)
        {
            if (monster == null) return;
            _activeMonsters.Remove(monster);
        }

        public void SetAIEnabled(bool enabled)
        {
            _aiEnabled = enabled;

            foreach (MonsterAI monster in _activeMonsters)
            {
                if (monster != null)
                    monster.SetDecisionEnabled(enabled);
            }
        }
    }
}
