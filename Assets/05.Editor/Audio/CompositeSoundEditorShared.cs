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

        public static void DrawSound(SerializedObject serializedSound)
        {
            if (serializedSound == null) return;

            EditorGUILayout.PropertyField(
                serializedSound.FindProperty("_clips"));
            EditorGUILayout.PropertyField(
                serializedSound.FindProperty("_volume"));
            EditorGUILayout.PropertyField(
                serializedSound.FindProperty("_pitchRange"));
            EditorGUILayout.PropertyField(
                serializedSound.FindProperty("_spatialBlend"));
            EditorGUILayout.PropertyField(
                serializedSound.FindProperty("_minimumDistance"));
            EditorGUILayout.PropertyField(
                serializedSound.FindProperty("_maximumDistance"));
            EditorGUILayout.PropertyField(
                serializedSound.FindProperty("_output"));
            EditorGUILayout.PropertyField(
                serializedSound.FindProperty("_positionOffset"));
        }

        public static CompositeSound CreateAsset(string defaultName)
        {
            string folder = ResolveCreationFolder();
            var sound = ScriptableObject.CreateInstance<CompositeSound>();
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{folder}/{defaultName}.asset");
            AssetDatabase.CreateAsset(sound, path);
            AssetDatabase.SaveAssets();
            return sound;
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
    }
}
