using UnityEditor;
using UnityEngine;
using ZZZ;
using ZZZ.Audio;
using ZZZ.Editor.Audio;

namespace ZZZ.Editor.AnimationTool
{
    public partial class AnimationConfigTool
    {
        private CompositeSound _soundComposite;
        private SerializedObject _soundCompositeObject;

        private void DrawSoundSection(TrackNotify notify)
        {
            if (!(notify.Payload is SoundNotifyPayload payload)) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            CompositeSound sound =
                (CompositeSound)EditorGUILayout.ObjectField(
                    "Sound (Composite)", payload.Sound,
                    typeof(CompositeSound), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Assign Composite Sound");
                payload.Sound = sound;
                EditorUtility.SetDirty(_config);
                RebuildSoundObject(sound);
            }

            if (GUILayout.Button("New", GUILayout.Width(40f)))
            {
                CompositeSound created =
                    CompositeSoundEditorShared.CreateAsset("Snd_New");
                Undo.RecordObject(_config, "Assign Composite Sound");
                payload.Sound = created;
                EditorUtility.SetDirty(_config);
                RebuildSoundObject(created);
                Selection.activeObject = created;
                EditorGUIUtility.PingObject(created);
            }
            EditorGUILayout.EndHorizontal();

            if (payload.Sound == null)
            {
                EditorGUILayout.HelpBox(
                    "CompositeSound를 지정하거나 New로 생성하세요.",
                    MessageType.Info);
                return;
            }

            if (_soundComposite != payload.Sound
                || _soundCompositeObject == null)
                RebuildSoundObject(payload.Sound);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                AssetDatabase.GetAssetPath(payload.Sound),
                EditorStyles.miniLabel);
            if (GUILayout.Button(
                    "Sound Tool", EditorStyles.miniButton,
                    GUILayout.Width(78f)))
            {
                Selection.activeObject = payload.Sound;
                SoundTool.Open();
            }
            EditorGUILayout.EndHorizontal();

            _soundCompositeObject.Update();
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "하나의 의미 단위로 재사용하며 Clips 중 하나를 무작위로 재생합니다. 여러 소리는 Sound Notify를 추가해 배치하세요.",
                MessageType.Info);
            CompositeSoundEditorShared.DrawSound(
                _soundCompositeObject);
            if (_soundCompositeObject.ApplyModifiedProperties())
                EditorUtility.SetDirty(payload.Sound);
        }

        private void RebuildSoundObject(CompositeSound sound)
        {
            _soundComposite = sound;
            _soundCompositeObject = sound != null
                ? new SerializedObject(sound)
                : null;
        }
    }
}
