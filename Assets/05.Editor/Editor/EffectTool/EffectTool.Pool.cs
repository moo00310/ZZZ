using UnityEditor;
using UnityEngine;
using ZZZ.Editor.Effects;

namespace ZZZ.Editor.EffectTool
{
    public partial class EffectTool
    {
        private Vector2 _poolScroll;

        private void DrawPoolOverview(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.20f, 0.20f, 0.20f));
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f));

            EditorGUILayout.LabelField("Pool Overview", EditorStyles.boldLabel);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("풀은 플레이 모드에서만 존재합니다. Play 후 이펙트가 재생되면 여기 표시됩니다.",
                    MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            Repaint();   // 매 프레임 최신 수치
            EffectEditorShared.DrawPoolTable(ref _poolScroll);

            GUILayout.EndArea();
        }
    }
}
