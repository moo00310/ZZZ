using System.Text;
using UnityEditor;
using UnityEngine;
using ZZZ;

namespace ZZZ.Editor.AnimationTool
{
    // 일회성 마이그레이션 — ClipLink의 레거시 입력 필드(Attack/Direction/RequireHeld)를 읽어
    // 폴리모픽 Condition(InputCondition)으로 이전한다. Condition이 이미 있으면 건드리지 않는다(멱등).
    // 모든 링크 이전이 검증되면 레거시 필드를 ClipLink에서 제거(2차 PR)한다.
    public static class ClipLinkConditionMigration
    {
        [MenuItem("ZZZ/Animation/Migrate ClipLink → Condition")]
        public static void Migrate()
        {
            var guids = AssetDatabase.FindAssets("t:AnimationConfig");
            int assetsChanged = 0, linksConverted = 0;
            var log = new StringBuilder();

            foreach (var guid in guids)
            {
                var path   = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<AnimationConfig>(path);
                if (config == null) continue;

                int before = linksConverted;

                if (config.Clips != null)
                    foreach (var clip in config.Clips)
                        linksConverted += ConvertLinks(clip?.Links);
                linksConverted += ConvertLinks(config.GlobalLinks);

                if (linksConverted > before)
                {
                    EditorUtility.SetDirty(config);
                    assetsChanged++;
                    log.AppendLine($"  {config.name}: +{linksConverted - before} links");
                }
            }

            if (assetsChanged > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[ClipLink Migration] {linksConverted} links → InputCondition "
                    + $"across {assetsChanged} configs.\n{log}");
        }

        private static int ConvertLinks(System.Collections.Generic.List<ClipLink> links)
        {
            if (links == null) return 0;
            int n = 0;
            foreach (var link in links)
            {
                if (link == null || link.Condition != null) continue;   // 멱등 — 이미 이전됨
                link.Condition = new InputCondition
                {
                    Attack      = link.Attack,
                    Direction   = link.Direction,
                    RequireHeld = link.RequireHeld,
                };
                n++;
            }
            return n;
        }
    }
}
