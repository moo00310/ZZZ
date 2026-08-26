using System;
using System.Collections.Generic;
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

        private void DrawSoundSection(TrackClip clip, TrackNotify notify)
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

            bool loop = payload.Loop;
            string nextSection = payload.NextSection;
            EditorGUI.BeginChangeCheck();
            loop = EditorGUILayout.Toggle(
                new GUIContent("Loop",
                    "Repeat from the Notify until its owning section is exited."),
                loop);
            if (loop)
                nextSection = DrawSoundCarrySection(clip, nextSection);
            else
                nextSection = "";
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Edit Sound Notify Loop");
                payload.Loop = loop;
                payload.NextSection = nextSection;
                EditorUtility.SetDirty(_config);
            }

            if (payload.Sound == null)
            {
                EditorGUILayout.HelpBox(
                    "CompositeSound를 지정하거나 New로 생성하세요.",
                    MessageType.Info);
                EditorGUILayout.Space(6f);
                DrawSoundModules(clip, payload, loop);
                return;
            }

            if (_soundComposite != payload.Sound
                || _soundCompositeObject == null)
                RebuildSoundObject(payload.Sound);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                AssetDatabase.GetAssetPath(payload.Sound),
                EditorStyles.miniLabel);
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

            EditorGUILayout.Space(6f);
            DrawSoundModules(clip, payload, loop);
        }

        private void DrawSoundModules(
            TrackClip clip, SoundNotifyPayload payload, bool loop)
        {
            for (int i = 0; i < payload.Modules.Count; i++)
            {
                SoundNotifyModule module = payload.Modules[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    module != null
                        ? ObjectNames.NicifyVariableName(module.GetType().Name)
                        : "Missing Sound Module",
                    EditorStyles.miniBoldLabel);
                if (GUILayout.Button(
                    "Remove", EditorStyles.miniButton,
                    GUILayout.Width(58f)))
                {
                    Undo.RecordObject(_config, "Remove Sound Module");
                    payload.Modules.RemoveAt(i);
                    EditorUtility.SetDirty(_config);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return;
                }
                EditorGUILayout.EndHorizontal();

                if (module is SoundFadeModule fadeModule)
                    DrawSoundFadeModule(fadeModule);
                else if (module is SoundDurationModule durationModule)
                    DrawSoundDurationModule(clip, durationModule);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button(
                "+ Add Sound Module", EditorStyles.miniButton))
            {
                var menu = new GenericMenu();
                foreach (Type type in
                    TypeCache.GetTypesDerivedFrom<SoundNotifyModule>())
                {
                    if (type.IsAbstract
                        || type.GetConstructor(Type.EmptyTypes) == null)
                        continue;

                    Type captured = type;
                    string label = ObjectNames.NicifyVariableName(type.Name);
                    if (HasSoundModule(payload, type))
                    {
                        menu.AddDisabledItem(new GUIContent(label), true);
                        continue;
                    }
                    menu.AddItem(new GUIContent(label), false, () =>
                    {
                        Undo.RecordObject(_config, "Add Sound Module");
                        payload.Modules.Add(
                            (SoundNotifyModule)Activator.CreateInstance(captured));
                        EditorUtility.SetDirty(_config);
                    });
                }
                menu.ShowAsContext();
            }
        }

        private void DrawSoundFadeModule(SoundFadeModule fadeModule)
        {
            float fadeInDuration = fadeModule.FadeInDuration;
            float fadeOutDuration = fadeModule.FadeOutDuration;
            EditorGUI.BeginChangeCheck();
            fadeInDuration = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Fade In (s)",
                    "Seconds to reach the CompositeSound volume."),
                fadeInDuration));
            fadeOutDuration = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Fade Out (s)",
                    "Seconds to fade after the owning section exits."),
                fadeOutDuration));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Edit Sound Fade Module");
                fadeModule.FadeInDuration = fadeInDuration;
                fadeModule.FadeOutDuration = fadeOutDuration;
                EditorUtility.SetDirty(_config);
            }
        }

        private void DrawSoundDurationModule(
            TrackClip clip, SoundDurationModule durationModule)
        {
            float duration = DurationFrameField(
                "Length (f)",
                "사운드가 완전히 끝나는 총 길이입니다. Fade Out 모듈이 있으면 종료 시점보다 앞서 자동으로 감쇠를 시작합니다.",
                clip, durationModule.Duration);
            if (Mathf.Approximately(duration, durationModule.Duration))
                return;

            Undo.RecordObject(_config, "Edit Sound Duration Module");
            durationModule.Duration = duration;
            EditorUtility.SetDirty(_config);
        }

        private static bool HasSoundModule(
            SoundNotifyPayload payload, Type type)
        {
            for (int i = 0; i < payload.Modules.Count; i++)
            {
                SoundNotifyModule module = payload.Modules[i];
                if (module != null && module.GetType() == type)
                    return true;
            }
            return false;
        }

        private static string DrawSoundCarrySection(
            TrackClip clip, string currentSection)
        {
            var sectionOptions = new List<string> { "" };
            for (int i = 0; i < clip.Links.Count; i++)
            {
                string targetSection = clip.Links[i].TargetSection;
                if (!string.IsNullOrEmpty(targetSection)
                    && !sectionOptions.Contains(targetSection))
                    sectionOptions.Add(targetSection);
            }
            if (!string.IsNullOrEmpty(currentSection)
                && !sectionOptions.Contains(currentSection))
                sectionOptions.Add(currentSection);

            string[] sectionLabels = new string[sectionOptions.Count];
            sectionLabels[0] = "(Current Section Only)";
            for (int i = 1; i < sectionOptions.Count; i++)
                sectionLabels[i] = sectionOptions[i];
            int selectedSection =
                Mathf.Max(0, sectionOptions.IndexOf(currentSection));
            selectedSection = EditorGUILayout.Popup(
                new GUIContent("Carry Section",
                    "Leave empty to stop on the current section exit. Select a destination to carry through its self-links and stop when that section exits."),
                selectedSection, sectionLabels);
            return sectionOptions[selectedSection];
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
