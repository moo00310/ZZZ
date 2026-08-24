using System.IO;
using UnityEditor;
using UnityEngine;
using ZZZ.Audio;

namespace ZZZ.Editor.Audio
{
    internal static class CompositeSoundEditorShared
    {
        private const string DEFAULT_ASSET_FOLDER =
            "Assets/02.Effects/Sounds";

        public static void DrawLayers(SerializedProperty layers)
        {
            if (layers == null) return;

            for (int i = 0; i < layers.arraySize; i++)
            {
                SerializedProperty layer =
                    layers.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    $"Layer {i + 1}", EditorStyles.miniBoldLabel);
                if (GUILayout.Button(
                        "Remove", EditorStyles.miniButton,
                        GUILayout.Width(58f)))
                {
                    Undo.RecordObject(
                        layers.serializedObject.targetObject,
                        "Remove Sound Layer");
                    layers.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(
                    layer, GUIContent.none, true);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button(
                    "+ Add Sound Layer", EditorStyles.miniButton))
                AddLayer(layers);
        }

        public static CompositeSound CreateAsset(string defaultName)
        {
            string folder = ResolveCreationFolder();
            var sound = ScriptableObject.CreateInstance<CompositeSound>();
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{folder}/{defaultName}.asset");
            AssetDatabase.CreateAsset(sound, path);
            AddDefaultLayer(sound);
            AssetDatabase.SaveAssets();
            return sound;
        }

        public static void AddDefaultLayer(CompositeSound sound)
        {
            if (sound == null) return;

            var serializedSound = new SerializedObject(sound);
            AddLayer(serializedSound.FindProperty("_layers"));
        }

        private static string ResolveCreationFolder()
        {
            string folder = DEFAULT_ASSET_FOLDER;
            var selected = Selection.activeObject;
            if (selected != null)
            {
                string selectedPath =
                    AssetDatabase.GetAssetPath(selected);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    folder = AssetDatabase.IsValidFolder(selectedPath)
                        ? selectedPath
                        : Path.GetDirectoryName(selectedPath);
                    if (!string.IsNullOrEmpty(folder))
                        folder = folder.Replace('\\', '/');
                }
            }

            return !string.IsNullOrEmpty(folder)
                && AssetDatabase.IsValidFolder(folder)
                ? folder
                : "Assets";
        }

        private static void AddLayer(SerializedProperty layers)
        {
            if (layers == null) return;

            Undo.RecordObject(
                layers.serializedObject.targetObject,
                "Add Sound Layer");
            int index = layers.arraySize;
            layers.arraySize++;
            SerializedProperty layer =
                layers.GetArrayElementAtIndex(index);
            SetDefaults(layer);
            layers.serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(
                layers.serializedObject.targetObject);
        }

        private static void SetDefaults(SerializedProperty layer)
        {
            layer.FindPropertyRelative("_startDelay").floatValue = 0f;
            layer.FindPropertyRelative("_clips").ClearArray();
            layer.FindPropertyRelative("_volume").floatValue = 1f;
            layer.FindPropertyRelative("_pitchRange").vector2Value =
                Vector2.one;
            layer.FindPropertyRelative("_spatialBlend").floatValue = 1f;
            layer.FindPropertyRelative("_minimumDistance").floatValue = 1f;
            layer.FindPropertyRelative("_maximumDistance").floatValue = 25f;
            layer.FindPropertyRelative("_output").objectReferenceValue = null;
            layer.FindPropertyRelative("_positionOffset").vector3Value =
                Vector3.zero;
        }
    }
}
