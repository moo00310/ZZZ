using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ.Effects;
using ZZZ.Editor.Effects;

namespace ZZZ.Editor.EffectTool
{
    public partial class EffectTool
    {
        private readonly HashSet<int> _entryCollapsed = new HashSet<int>();
        private readonly HashSet<int> _poolFold       = new HashSet<int>();

        private void DrawInspector(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.22f, 0.22f, 0.22f));
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f));
            _inspScroll = EditorGUILayout.BeginScrollView(_inspScroll);

            if (_selectedComposite != null) DrawCompositeInspector();
            else EditorGUILayout.HelpBox("좌측에서 조합(Composite)을 선택하거나 New Composite로 만드세요.", MessageType.Info);

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCompositeInspector()
        {
            if (_compositeSO == null) RebuildSelectionSO();
            if (_compositeSO == null) return;
            _compositeSO.Update();

            EditorGUILayout.LabelField(_selectedComposite.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("프리팹들을 상대 시차·배치로 묶는 조합. 타임라인에서 시차 드래그.",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4f);

            var entries = _compositeSO.FindProperty("Entries");

            for (int i = 0; i < entries.arraySize; i++)
            {
                var e = entries.GetArrayElementAtIndex(i);
                var prefabProp = e.FindPropertyRelative("Prefab");
                var prefab = prefabProp.objectReferenceValue as GameObject;
                string title = prefab != null ? prefab.name : "(프리팹 미지정)";
                float delay = e.FindPropertyRelative("StartDelay").floatValue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                bool expanded = !_entryCollapsed.Contains(i);
                bool newExp = EditorGUILayout.Foldout(expanded, $"#{i}  {title}   @{delay:0.##}s", true);
                if (newExp != expanded) { if (newExp) _entryCollapsed.Remove(i); else _entryCollapsed.Add(i); }
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.8f, 0.35f, 0.35f);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    entries.DeleteArrayElementAtIndex(i);
                    _compositeSO.ApplyModifiedProperties();
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                if (newExp)
                {
                    EditorGUILayout.PropertyField(prefabProp, new GUIContent("Prefab"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("StartDelay"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("Socket"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("PositionOffset"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("EulerOffset"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("Scale"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("FollowSpawner"));

                    bool poolFold = _poolFold.Contains(i);
                    bool newPool = EditorGUILayout.Foldout(poolFold, "풀링/반납 설정", true);
                    if (newPool != poolFold) { if (newPool) _poolFold.Add(i); else _poolFold.Remove(i); }
                    if (newPool)
                    {
                        EditorGUI.indentLevel++;
                        EffectEditorShared.DrawEntryPoolFields(e, prefab);
                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2f);
            }

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("+ Add Entry"))
            {
                int n = entries.arraySize;
                entries.InsertArrayElementAtIndex(n);
                var e = entries.GetArrayElementAtIndex(n);
                e.FindPropertyRelative("Prefab").objectReferenceValue = null;
                e.FindPropertyRelative("StartDelay").floatValue = 0f;
                e.FindPropertyRelative("Socket").stringValue = "";
                e.FindPropertyRelative("PositionOffset").vector3Value = Vector3.zero;
                e.FindPropertyRelative("EulerOffset").vector3Value = Vector3.zero;
                e.FindPropertyRelative("Scale").vector3Value = Vector3.one;
                e.FindPropertyRelative("FollowSpawner").boolValue = false;
                e.FindPropertyRelative("PrewarmCount").intValue = 4;
                e.FindPropertyRelative("MaxSize").intValue = 0;
                e.FindPropertyRelative("Despawn").enumValueIndex = (int)DespawnMode.ParticleStopped;
                e.FindPropertyRelative("Lifetime").floatValue = 0f;
            }

            _compositeSO.ApplyModifiedProperties();
        }
    }
}
