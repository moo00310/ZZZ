using UnityEditor;
using UnityEngine;
using ZZZ.Effects;

namespace ZZZ.Editor.EffectTool
{
    public partial class EffectTool
    {
        private static readonly Color RowSelected = new Color(0.24f, 0.40f, 0.62f);
        private static readonly Color RowHover    = new Color(1f, 1f, 1f, 0.05f);

        private void DrawList(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.19f, 0.19f, 0.19f));
            GUILayout.BeginArea(area);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            Rect header = GUILayoutUtility.GetRect(0, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(header, new Color(0.14f, 0.14f, 0.14f));
            GUI.Label(new Rect(header.x + 4f, header.y + 2f, header.width - 4f, 16f),
                $"Composites ({_composites.Count})", EditorStyles.boldLabel);

            foreach (var c in _composites)
            {
                if (c == null) continue;
                Rect row = GUILayoutUtility.GetRect(area.width, 20f);
                bool sel = _selectedComposite == c;
                if (sel) EditorGUI.DrawRect(row, RowSelected);
                else if (row.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(row, RowHover);

                GUI.Label(new Rect(row.x + 8f, row.y + 1f, row.width - 12f, 18f), c.name, EditorStyles.label);

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                    && row.Contains(Event.current.mousePosition))
                {
                    SelectComposite(c);
                    Event.current.Use();
                }
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
