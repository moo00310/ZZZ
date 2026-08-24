using UnityEditor;
using UnityEngine;
using ZZZ.Audio;

namespace ZZZ.Editor.Audio
{
    [CustomEditor(typeof(CompositeSound))]
    public sealed class CompositeSoundEditor : UnityEditor.Editor
    {
        private SerializedProperty _layers;

        private void OnEnable()
        {
            _layers = serializedObject.FindProperty("_layers");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "여러 Sound Layer를 시간차로 겹쳐 하나의 재사용 가능한 사운드를 구성합니다.",
                MessageType.Info);
            CompositeSoundEditorShared.DrawLayers(_layers);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
