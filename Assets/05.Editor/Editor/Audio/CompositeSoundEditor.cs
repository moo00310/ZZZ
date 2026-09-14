using UnityEditor;
using ZZZ.Audio;

namespace ZZZ.Editor.Audio
{
    [CustomEditor(typeof(CompositeSound))]
    public sealed class CompositeSoundEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "발걸음, 칼 소리, 패링처럼 하나의 의미 단위로 재사용하는 사운드입니다. Clips 중 하나를 무작위로 재생합니다.",
                MessageType.Info);
            CompositeSoundEditorShared.DrawSound(serializedObject);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
